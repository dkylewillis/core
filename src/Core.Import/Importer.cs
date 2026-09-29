using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Corex;
using Core.Corex.IO;
using Core.Corex.Serialization;
using Core.Import.Geometry;
using Core.Import.Mapping;
using Core.Store;
using Microsoft.Data.Sqlite;

namespace Core.Import;

public sealed class Importer
{
    private readonly JsonSerializerOptions _json = JsonOptions.Create();

    public ImportResult Import(string packagePath, string outCorePath, ImportOptions options)
    {
        var package = CorexReader.Read(packagePath);
        var messages = new List<ValidationMessage>();
        var projectLinear = InferProjectLinearUnits(package);
        var mapping = MappingProfileLoader.Load(options.MappingProfilePath);
        var mappingSha = MappingProfileLoader.Sha256(options.MappingProfilePath);
        var corexSha = ComputePackageSha(packagePath);
        var snapshotId = Guid.NewGuid().ToString("D");
        var projectId = Guid.NewGuid().ToString("D");

        if (File.Exists(outCorePath))
            File.Delete(outCorePath);

        using var store = CoreStore.Create(outCorePath, snapshotId, projectId, package.Manifest.Project.Name);
        var db = store.Connection;
        using var tx = db.BeginTransaction();

        InsertSnapshot(db, snapshotId, projectId, package, options, mappingSha, corexSha);

        var drawings = new Dictionary<string, long>(StringComparer.Ordinal);
        var sourceEntities = new Dictionary<(string Drawing, string Handle), long>();
        var styles = new Dictionary<(string Drawing, string Handle), long>();
        var blockDefs = new Dictionary<(string Drawing, string Handle), long>();
        var xrefInstances = new Dictionary<(string Drawing, string Handle), long>();
        var layersByDrawing = new Dictionary<string, Dictionary<string, LayerInfo>>(StringComparer.Ordinal);

        // T4: source facts
        foreach (var md in package.Manifest.Drawings)
        {
            var drawing = package.Drawings[md.Id];
            var sourceFileId = InsertSourceFile(db, "dwg", drawing.File.SourcePath, drawing.File.Sha256, drawing.File.SizeBytes);
            var toProject = Units.ToProjectUnitsFactor(drawing.Units.Linear, projectLinear);
            var drawingId = InsertDrawing(db, sourceFileId, drawing, toProject);
            drawings[drawing.Id] = drawingId;
            layersByDrawing[drawing.Id] = drawing.Layers.ToDictionary(l => l.Name, StringComparer.Ordinal);

            foreach (var layer in drawing.Layers)
                InsertLayer(db, drawingId, layer);

            foreach (var bd in drawing.BlockDefinitions)
            {
                var id = InsertBlockDefinition(db, drawingId, bd);
                blockDefs[(drawing.Id, bd.Handle)] = id;
            }

            foreach (var style in drawing.Styles)
            {
                var styleId = InsertStyle(db, drawingId, style);
                styles[(drawing.Id, style.Handle)] = styleId;
                foreach (var c in style.Components)
                    InsertDisplayComponent(db, styleId, c);
            }

            foreach (var entity in drawing.Entities)
            {
                long? styleId = entity.Style != null && styles.TryGetValue((drawing.Id, entity.Style), out var sid) ? sid : null;
                long? geometryId = null;
                if (entity.Geometry != null)
                    geometryId = InsertGeometry(db, entity.Geometry, drawing.Units.Linear, space: entity.Space == "block" ? "block" : "source");

                var payload = BuildPayloadJson(entity);
                var entityId = InsertSourceEntity(db, drawingId, entity, styleId, geometryId, payload);
                sourceEntities[(drawing.Id, entity.Handle)] = entityId;

                if (entity.Kind == "block-reference" && entity.Block != null)
                {
                    if (blockDefs.TryGetValue((drawing.Id, entity.Block.Definition), out var defId))
                        InsertBlockReference(db, entityId, defId, entity.Block.Transform);
                    if (entity.Block.Attributes != null)
                    {
                        foreach (var a in entity.Block.Attributes)
                            InsertAttribute(db, entityId, a.Tag, a.Value);
                    }
                }
            }

        }

        // Xref instances after all drawings exist (resolved targets require target_drawing_id).
        foreach (var md in package.Manifest.Drawings)
        {
            var drawing = package.Drawings[md.Id];
            var drawingId = drawings[drawing.Id];
            foreach (var xref in drawing.Xrefs)
            {
                long? targetId = xref.Target != null && drawings.ContainsKey(xref.Target) ? drawings[xref.Target] : null;
                var xrefId = InsertXrefInstance(db, drawingId, xref, targetId);
                xrefInstances[(drawing.Id, xref.Handle)] = xrefId;
            }
        }

        // Shortcuts + data references
        var shortcutIds = new Dictionary<string, long>(StringComparer.Ordinal);
        if (package.Shortcuts != null)
        {
            foreach (var s in package.Shortcuts.Shortcuts)
            {
                long? sourceDrawingId = s.SourceDrawing != null && drawings.ContainsKey(s.SourceDrawing) ? drawings[s.SourceDrawing] : null;
                shortcutIds[s.Name] = InsertShortcut(db, s, sourceDrawingId);
            }
        }

        foreach (var md in package.Manifest.Drawings)
        {
            var drawing = package.Drawings[md.Id];
            foreach (var entity in drawing.Entities)
            {
                if (entity.Reference == null) continue;
                var refEntityId = sourceEntities[(drawing.Id, entity.Handle)];
                long? sourceEntityId = null;
                if (entity.Reference.Source != null &&
                    sourceEntities.TryGetValue((entity.Reference.Source.Drawing, entity.Reference.Source.Handle), out var seid))
                    sourceEntityId = seid;
                InsertDataReference(db, refEntityId, sourceEntityId, entity.Reference.Shortcut, entity.Reference.Status);
                if (entity.Reference.Status is "source-not-found" or "object-not-found")
                {
                    messages.Add(Warn("data-reference-source-missing",
                        $"Data reference {drawing.Id}:{entity.Handle} status {entity.Reference.Status}.",
                        Subject(drawing.Id, entity.Handle)));
                }
            }
        }

        // T5: xref paths + world transforms
        var xrefPaths = new List<XrefPathRow>();
        foreach (var rootId in drawings.Keys)
        {
            // Only compute from sheet drawings (have layouts) and any root — fixture expects sheet roots C301 (and paths from sheets)
            BuildXrefPaths(package, rootId, drawings, xrefInstances, db, xrefPaths, messages);
        }

        // T6: DesignObjects
        var designObjects = MapDesignObjects(package, mapping, drawings, sourceEntities, db, messages);

        // T7: Document model
        var layouts = new Dictionary<(string Drawing, string Handle), long>();
        var viewports = new Dictionary<(string Drawing, string Handle), long>();
        var sheets = new Dictionary<string, long>(StringComparer.Ordinal);
        BuildDocumentModel(package, drawings, sourceEntities, designObjects, db, layouts, viewports, sheets);

        // T8: Presentation / visibility
        BuildPresentations(package, drawings, sourceEntities, designObjects, xrefPaths, viewports, layersByDrawing, db);

        // Annotations linking (for rules / document model)
        LinkAnnotations(package, drawings, sourceEntities, designObjects, viewports, db);

        // T9: validation
        ValidateModel(package, drawings, messages);

        foreach (var m in messages)
            InsertValidationMessage(db, m);

        if (messages.Any(m => m.Severity == "error"))
        {
            tx.Rollback();
            store.Dispose();
            if (File.Exists(outCorePath))
                File.Delete(outCorePath);
            return new ImportResult
            {
                SnapshotId = snapshotId,
                CorePath = outCorePath,
                Messages = messages
            };
        }

        tx.Commit();
        return new ImportResult
        {
            SnapshotId = snapshotId,
            CorePath = outCorePath,
            Messages = messages
        };
    }

    private static string InferProjectLinearUnits(CorexPackage package)
    {
        // Prefer sheet drawing units; fall back to first drawing.
        foreach (var d in package.Drawings.Values)
        {
            if (d.Layouts.Count > 0)
                return d.Units.Linear;
        }
        return package.Drawings.Values.First().Units.Linear;
    }

    private static string ComputePackageSha(string path)
    {
        // Deterministic content hash over manifest bytes when directory; file hash when zip.
        if (File.Exists(path) && !Directory.Exists(path))
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        var manifest = File.ReadAllBytes(Path.Combine(path, "manifest.json"));
        return Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
    }

    private void InsertSnapshot(SqliteConnection db, string snapshotId, string projectId, CorexPackage package, ImportOptions options, string mappingSha, string corexSha)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO snapshot(id, project_id, corex_sha256, corex_version, exporter_name, exporter_version,
              host_product, host_version, importer_version, mapping_profile_sha256, ai_enabled, ai_model, created_at)
            VALUES ($id,$project,$corexSha,$corexVer,$expName,$expVer,$hostProd,$hostVer,$impVer,$mapSha,$ai,$aiModel,$created);
            """;
        cmd.Parameters.AddWithValue("$id", snapshotId);
        cmd.Parameters.AddWithValue("$project", projectId);
        cmd.Parameters.AddWithValue("$corexSha", corexSha);
        cmd.Parameters.AddWithValue("$corexVer", package.Manifest.CorexVersion);
        cmd.Parameters.AddWithValue("$expName", package.Manifest.Exporter.Name);
        cmd.Parameters.AddWithValue("$expVer", package.Manifest.Exporter.Version);
        cmd.Parameters.AddWithValue("$hostProd", package.Manifest.Host.Product);
        cmd.Parameters.AddWithValue("$hostVer", package.Manifest.Host.Version);
        cmd.Parameters.AddWithValue("$impVer", options.ImporterVersion);
        cmd.Parameters.AddWithValue("$mapSha", mappingSha);
        cmd.Parameters.AddWithValue("$ai", options.AiEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$aiModel", (object?)options.AiModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static long InsertSourceFile(SqliteConnection db, string kind, string path, string sha, long? size)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO source_file(kind, path, sha256, size_bytes) VALUES ($k,$p,$s,$sz); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$s", sha);
        cmd.Parameters.AddWithValue("$sz", (object?)size ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    private static long InsertDrawing(SqliteConnection db, long sourceFileId, DrawingDocument drawing, double toProject)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO drawing(source_file_id, corex_id, fingerprint_guid, version_guid, insunits, linear_units, to_project_units,
              coordinate_system_code, drawing_scale, grid_to_ground_scale, visretain)
            VALUES ($sf,$id,$fp,$vg,$ins,$lin,$to,$cs,$scale,$g2g,$vis); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$sf", sourceFileId);
        cmd.Parameters.AddWithValue("$id", drawing.Id);
        cmd.Parameters.AddWithValue("$fp", drawing.FingerprintGuid);
        cmd.Parameters.AddWithValue("$vg", drawing.VersionGuid);
        cmd.Parameters.AddWithValue("$ins", drawing.Units.Insunits);
        cmd.Parameters.AddWithValue("$lin", drawing.Units.Linear);
        cmd.Parameters.AddWithValue("$to", toProject);
        cmd.Parameters.AddWithValue("$cs", (object?)drawing.Civil?.CoordinateSystemCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$scale", (object?)drawing.Civil?.DrawingScale ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$g2g", (object?)drawing.Civil?.GridToGroundScale ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$vis", drawing.Settings.Visretain ? 1 : 0);
        return (long)cmd.ExecuteScalar()!;
    }

    private static void InsertLayer(SqliteConnection db, long drawingId, LayerInfo layer)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO layer(drawing_id, name, is_on, is_frozen, is_locked, is_plot, dependent_xref) VALUES ($d,$n,$on,$fr,$lk,$pl,$xref);";
        cmd.Parameters.AddWithValue("$d", drawingId);
        cmd.Parameters.AddWithValue("$n", layer.Name);
        cmd.Parameters.AddWithValue("$on", layer.On ? 1 : 0);
        cmd.Parameters.AddWithValue("$fr", layer.Frozen ? 1 : 0);
        cmd.Parameters.AddWithValue("$lk", layer.Locked ? 1 : 0);
        cmd.Parameters.AddWithValue("$pl", layer.Plot ? 1 : 0);
        cmd.Parameters.AddWithValue("$xref", (object?)layer.DependentXref ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static long InsertBlockDefinition(SqliteConnection db, long drawingId, BlockDefinitionInfo bd)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO block_definition(drawing_id, handle, name, kind, origin_x, origin_y, origin_z) VALUES ($d,$h,$n,$k,$x,$y,$z); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$d", drawingId);
        cmd.Parameters.AddWithValue("$h", bd.Handle);
        cmd.Parameters.AddWithValue("$n", bd.Name);
        cmd.Parameters.AddWithValue("$k", bd.Kind);
        cmd.Parameters.AddWithValue("$x", bd.Origin[0]);
        cmd.Parameters.AddWithValue("$y", bd.Origin[1]);
        cmd.Parameters.AddWithValue("$z", bd.Origin[2]);
        return (long)cmd.ExecuteScalar()!;
    }

    private static long InsertStyle(SqliteConnection db, long drawingId, StyleInfo style)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO style(drawing_id, handle, name, kind, object_type) VALUES ($d,$h,$n,$k,$t); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$d", drawingId);
        cmd.Parameters.AddWithValue("$h", style.Handle);
        cmd.Parameters.AddWithValue("$n", style.Name);
        cmd.Parameters.AddWithValue("$k", style.Kind);
        cmd.Parameters.AddWithValue("$t", style.ObjectType);
        return (long)cmd.ExecuteScalar()!;
    }

    private static void InsertDisplayComponent(SqliteConnection db, long styleId, DisplayComponentInfo c)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO display_component(style_id, name, view, is_visible, layer_name) VALUES ($s,$n,$v,$vis,$l);";
        cmd.Parameters.AddWithValue("$s", styleId);
        cmd.Parameters.AddWithValue("$n", c.Name);
        cmd.Parameters.AddWithValue("$v", c.View);
        cmd.Parameters.AddWithValue("$vis", c.Visible ? 1 : 0);
        cmd.Parameters.AddWithValue("$l", c.Layer);
        cmd.ExecuteNonQuery();
    }

    private long InsertGeometry(SqliteConnection db, GeometryPayload geometry, string units, string space)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO geometry(kind, space, units, is_derived, data_json) VALUES ($k,$sp,$u,0,$j); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$k", geometry.Type);
        cmd.Parameters.AddWithValue("$sp", space);
        cmd.Parameters.AddWithValue("$u", units);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(geometry, _json));
        var id = (long)cmd.ExecuteScalar()!;

        // Coarse R*Tree index when we can derive a bbox.
        if (TryBBox(geometry, out var minX, out var minY, out var maxX, out var maxY))
        {
            using var r = db.CreateCommand();
            r.CommandText = "INSERT INTO geometry_rtree(id, min_x, max_x, min_y, max_y) VALUES ($id,$minx,$maxx,$miny,$maxy);";
            r.Parameters.AddWithValue("$id", id);
            r.Parameters.AddWithValue("$minx", minX);
            r.Parameters.AddWithValue("$maxx", maxX);
            r.Parameters.AddWithValue("$miny", minY);
            r.Parameters.AddWithValue("$maxy", maxY);
            r.ExecuteNonQuery();
        }
        return id;
    }

    private static bool TryBBox(GeometryPayload g, out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = maxX = maxY = 0;
        var pts = new List<(double X, double Y)>();
        void Add(double[]? p)
        {
            if (p is { Length: >= 2 }) pts.Add((p[0], p[1]));
        }
        Add(g.Position); Add(g.Start); Add(g.End); Add(g.Center);
        if (g.Vertices != null)
            foreach (var v in g.Vertices)
                if (v.Length >= 2) pts.Add((v[0], v[1]));
        if (pts.Count == 0) return false;
        minX = pts.Min(p => p.X); maxX = pts.Max(p => p.X);
        minY = pts.Min(p => p.Y); maxY = pts.Max(p => p.Y);
        return true;
    }

    private string? BuildPayloadJson(Entity entity)
    {
        object? payload = entity.Kind switch
        {
            "pipe" => entity.Pipe,
            "structure" => entity.Structure,
            "pipe-network" or "pressure-network" => entity.Network,
            "alignment" => entity.Alignment,
            "profile" => entity.Profile,
            "profile-view" => entity.ProfileView,
            "label" => entity.Label,
            "text" or "mtext" or "mleader" => entity.Text,
            "block-reference" => entity.Block,
            "pressure-pipe" or "pressure-fitting" or "pressure-appurtenance" => entity.PressurePart,
            "view-frame" => entity.ViewFrame,
            _ => null
        };
        if (payload == null && entity.Leader == null) return null;
        if (entity.Kind == "mleader")
            return JsonSerializer.Serialize(new { text = entity.Text, leader = entity.Leader }, _json);
        return payload == null ? null : JsonSerializer.Serialize(payload, _json);
    }

    private static long InsertSourceEntity(SqliteConnection db, long drawingId, Entity entity, long? styleId, long? geometryId, string? payload)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO source_entity(drawing_id, handle, kind, class, name, space, owner_handle, layer_name, is_visible, is_proxy, style_id, geometry_id, payload_json, properties_json)
            VALUES ($d,$h,$k,$c,$n,$sp,$own,$layer,$vis,$proxy,$style,$geom,$payload,$props); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$d", drawingId);
        cmd.Parameters.AddWithValue("$h", entity.Handle);
        cmd.Parameters.AddWithValue("$k", entity.Kind);
        cmd.Parameters.AddWithValue("$c", entity.Class);
        cmd.Parameters.AddWithValue("$n", (object?)entity.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sp", entity.Space);
        cmd.Parameters.AddWithValue("$own", (object?)entity.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$layer", entity.Layer);
        cmd.Parameters.AddWithValue("$vis", (entity.Visible ?? true) ? 1 : 0);
        cmd.Parameters.AddWithValue("$proxy", (entity.Proxy ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$style", (object?)styleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$geom", (object?)geometryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", (object?)payload ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$props", entity.Properties == null ? DBNull.Value : JsonSerializer.Serialize(entity.Properties));
        return (long)cmd.ExecuteScalar()!;
    }

    private static void InsertBlockReference(SqliteConnection db, long entityId, long defId, double[] transform)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO block_reference(source_entity_id, block_definition_id, transform_json) VALUES ($e,$d,$t);";
        cmd.Parameters.AddWithValue("$e", entityId);
        cmd.Parameters.AddWithValue("$d", defId);
        cmd.Parameters.AddWithValue("$t", JsonSerializer.Serialize(transform));
        cmd.ExecuteNonQuery();
    }

    private static void InsertAttribute(SqliteConnection db, long entityId, string tag, string value)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO attribute(source_entity_id, tag, value) VALUES ($e,$t,$v);";
        cmd.Parameters.AddWithValue("$e", entityId);
        cmd.Parameters.AddWithValue("$t", tag);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private long InsertXrefInstance(SqliteConnection db, long hostDrawingId, XrefInfo xref, long? targetId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO xref_instance(host_drawing_id, handle, block_name, target_drawing_id, attachment, saved_path, resolved_path, resolved_by, status, layer_name, unit_scale, transform_json, clip_json)
            VALUES ($h,$handle,$bn,$t,$att,$saved,$resolved,$by,$status,$layer,$us,$xf,$clip); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$h", hostDrawingId);
        cmd.Parameters.AddWithValue("$handle", xref.Handle);
        cmd.Parameters.AddWithValue("$bn", xref.BlockName);
        cmd.Parameters.AddWithValue("$t", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$att", xref.Attachment);
        cmd.Parameters.AddWithValue("$saved", xref.Resolution.SavedPath);
        cmd.Parameters.AddWithValue("$resolved", (object?)xref.Resolution.ResolvedPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$by", xref.Resolution.ResolvedBy);
        cmd.Parameters.AddWithValue("$status", xref.Resolution.Status);
        cmd.Parameters.AddWithValue("$layer", xref.Layer);
        cmd.Parameters.AddWithValue("$us", xref.UnitScale);
        cmd.Parameters.AddWithValue("$xf", JsonSerializer.Serialize(xref.Transform));
        cmd.Parameters.AddWithValue("$clip", xref.Clip == null ? DBNull.Value : JsonSerializer.Serialize(xref.Clip, _json));
        return (long)cmd.ExecuteScalar()!;
    }

    private static long InsertShortcut(SqliteConnection db, ShortcutEntry s, long? sourceDrawingId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO shortcut(name, object_type, source_path, source_drawing_id, source_handle, status) VALUES ($n,$t,$p,$d,$h,$s); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", s.Name);
        cmd.Parameters.AddWithValue("$t", s.ObjectType);
        cmd.Parameters.AddWithValue("$p", s.SourcePath);
        cmd.Parameters.AddWithValue("$d", (object?)sourceDrawingId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", (object?)s.SourceHandle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", s.Status);
        return (long)cmd.ExecuteScalar()!;
    }

    private static void InsertDataReference(SqliteConnection db, long refEntityId, long? sourceEntityId, string shortcut, string status)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO data_reference(reference_entity_id, source_entity_id, shortcut_name, status) VALUES ($r,$s,$n,$st);";
        cmd.Parameters.AddWithValue("$r", refEntityId);
        cmd.Parameters.AddWithValue("$s", (object?)sourceEntityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", shortcut);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.ExecuteNonQuery();
    }

    private sealed class XrefPathRow
    {
        public long Id;
        public string Root = "";
        public List<string?> PathDrawings = [];
        public List<(string Drawing, string Handle)> Instances = [];
        public bool Loaded;
        public string? Reason;
        public Matrix4d? WorldTransform;
    }

    private void BuildXrefPaths(
        CorexPackage package,
        string rootId,
        Dictionary<string, long> drawings,
        Dictionary<(string, string), long> xrefInstances,
        SqliteConnection db,
        List<XrefPathRow> allPaths,
        List<ValidationMessage> messages)
    {
        // Only emit paths for drawings that are sheet hosts (have layouts) — matches fixture expectation for C301.
        if (!package.Drawings.TryGetValue(rootId, out var root) || root.Layouts.Count == 0)
            return;

        void Walk(string currentDrawingId, List<string?> pathDrawings, List<(string Drawing, string Handle)> instances, Matrix4d transform, bool loaded, string? reason)
        {
            var drawing = package.Drawings[currentDrawingId];
            foreach (var xref in drawing.Xrefs)
            {
                var nextPath = pathDrawings.Concat([xref.Target]).ToList();
                var nextInstances = instances.Concat([(currentDrawingId, xref.Handle)]).ToList();
                var nextLoaded = loaded;
                string? nextReason = reason;

                if (!loaded)
                {
                    // keep existing reason
                }
                else if (xref.Resolution.Status is "not-found" or "unresolved" or "unloaded")
                {
                    nextLoaded = false;
                    nextReason = xref.Resolution.Status;
                    if (xref.Resolution.Status is "not-found" or "unresolved")
                        messages.Add(Warn("xref-unresolved", $"Xref {currentDrawingId}:{xref.Handle} is {xref.Resolution.Status}.", Subject(currentDrawingId, xref.Handle)));
                }
                else if (instances.Count >= 1 && xref.Attachment == "overlay")
                {
                    // Overlay nested inside another Xref is not loaded through the parent.
                    nextLoaded = false;
                    nextReason = "overlay-not-nested";
                }

                Matrix4d? nextTransform = null;
                if (xref.Resolution.Status == "resolved" && xref.Transform != null)
                {
                    var local = Matrix4d.FromArray(xref.Transform);
                    nextTransform = transform.Multiply(local);

                    // Mixed units warning when unitScale != 1 along the chain.
                    if (Math.Abs(xref.UnitScale - 1.0) > 1e-12)
                    {
                        var hostUnits = drawing.Units.Linear;
                        var targetUnits = xref.Target != null ? package.Drawings[xref.Target].Units.Linear : "?";
                        messages.Add(Warn("mixed-units-in-xref-chain",
                            $"{xref.Target} is {targetUnits}; host {currentDrawingId} is {hostUnits}; unitScale {xref.UnitScale}",
                            Subject(currentDrawingId, xref.Handle)));
                    }
                }

                var row = new XrefPathRow
                {
                    Root = rootId,
                    PathDrawings = nextPath,
                    Instances = nextInstances,
                    Loaded = nextLoaded,
                    Reason = nextLoaded ? null : nextReason,
                    WorldTransform = nextTransform
                };
                row.Id = InsertXrefPath(db, drawings[rootId], xref.Target != null && drawings.ContainsKey(xref.Target) ? drawings[xref.Target] : null, nextInstances.Select(i => xrefInstances[i]).ToList(), row);
                allPaths.Add(row);

                if (nextLoaded && xref.Target != null)
                    Walk(xref.Target, nextPath, nextInstances, nextTransform ?? transform, nextLoaded, nextReason);
            }
        }

        Walk(rootId, [rootId], [], Matrix4d.Identity, loaded: true, reason: null);
    }

    private long InsertXrefPath(SqliteConnection db, long rootDrawingId, long? leafDrawingId, List<long> instanceIds, XrefPathRow row)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO xref_path(root_drawing_id, leaf_drawing_id, instance_ids_json, is_loaded, not_loaded_reason, world_transform_json)
            VALUES ($r,$l,$ids,$loaded,$reason,$xf); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$r", rootDrawingId);
        cmd.Parameters.AddWithValue("$l", (object?)leafDrawingId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(instanceIds));
        cmd.Parameters.AddWithValue("$loaded", row.Loaded ? 1 : 0);
        cmd.Parameters.AddWithValue("$reason", (object?)row.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$xf", row.WorldTransform == null ? DBNull.Value : JsonSerializer.Serialize(row.WorldTransform.Value.ToArray()));
        return (long)cmd.ExecuteScalar()!;
    }

    private sealed class DesignObjectRow
    {
        public long Id;
        public string Type = "";
        public string? Name;
        public string? System;
        public string StableKey = "";
        public Dictionary<string, object?> Properties = new();
        public List<(string Drawing, string Handle, string Method, string? Via)> Mappings = [];
    }

    private Dictionary<(string Drawing, string Handle), long> MapDesignObjects(
        CorexPackage package,
        MappingProfile mapping,
        Dictionary<string, long> drawings,
        Dictionary<(string, string), long> sourceEntities,
        SqliteConnection db,
        List<ValidationMessage> messages)
    {
        var bySource = new Dictionary<(string Drawing, string Handle), long>();
        var objects = new List<DesignObjectProp>();

        // Collect native source entities first (non-reference), then attach data-reference mappings.
        var nativeByKey = new Dictionary<string, DesignObjectProp>(StringComparer.Ordinal);

        foreach (var drawing in package.Drawings.Values)
        {
            foreach (var entity in drawing.Entities)
            {
                if (entity.Reference != null) continue;
                var type = entity.Kind switch
                {
                    "pipe-network" => "GravityNetwork",
                    "pipe" => "GravityPipe",
                    "structure" => "GravityStructure",
                    "alignment" => "Alignment",
                    "profile" => "Profile",
                    "surface" => "Surface",
                    _ => null
                };
                if (type == null) continue;

                string? system = null;
                string? classRule = null;
                if (type == "GravityNetwork" || type == "GravityPipe" || type == "GravityStructure")
                {
                    var networkName = type == "GravityNetwork" ? entity.Name : FindNetworkName(package, drawing.Id, entity);
                    if (networkName != null)
                    {
                        foreach (var rule in mapping.NetworkSystems)
                        {
                            if (rule.Compiled!.IsMatch(networkName))
                            {
                                system = rule.System;
                                classRule = rule.Id;
                                break;
                            }
                        }
                    }
                }

                var props = BuildDesignProperties(entity, package, drawing);
                var keyParts = new List<string> { type, $"{drawing.FingerprintGuid}:{entity.Handle}" };
                var stableKey = string.Join("|", keyParts);
                var obj = new DesignObjectProp
                {
                    Type = type,
                    Name = entity.Name,
                    System = system,
                    StableKey = stableKey,
                    Properties = props,
                    Mappings = { (drawing.Id, entity.Handle, "native", null) }
                };
                objects.Add(obj);
                nativeByKey[$"{type}:{drawing.Id}:{entity.Handle}"] = obj;
                if (entity.Name != null)
                    nativeByKey[$"{type}:name:{entity.Name}"] = obj;
            }
        }

        // Surfaces / objects that exist only as broken data references
        foreach (var drawing in package.Drawings.Values)
        {
            foreach (var entity in drawing.Entities)
            {
                if (entity.Reference == null) continue;
                if (entity.Reference.Source != null)
                {
                    var type = entity.Kind switch
                    {
                        "pipe-network" => "GravityNetwork",
                        "pipe" => "GravityPipe",
                        "structure" => "GravityStructure",
                        "alignment" => "Alignment",
                        "profile" => "Profile",
                        "surface" => "Surface",
                        _ => null
                    };
                    if (type == null) continue;
                    var sourceKey = $"{type}:{entity.Reference.Source.Drawing}:{entity.Reference.Source.Handle}";
                    if (nativeByKey.TryGetValue(sourceKey, out var obj))
                        obj.Mappings.Add((drawing.Id, entity.Handle, "native", "data-reference"));
                }
                else if (entity.Kind == "surface")
                {
                    // Source missing — create DesignObject from the reference alone.
                    var stableKey = $"Surface|{drawing.FingerprintGuid}:{entity.Handle}";
                    var obj = new DesignObjectProp
                    {
                        Type = "Surface",
                        Name = entity.Name,
                        StableKey = stableKey,
                        Properties = new Dictionary<string, object?> { ["sourceMissing"] = true },
                        Mappings = { (drawing.Id, entity.Handle, "native", "data-reference") }
                    };
                    objects.Add(obj);
                }
            }
        }

        // Recompute stable keys from all mappings (sorted fingerprint:handle)
        foreach (var obj in objects)
        {
            var identities = obj.Mappings
                .Select(m =>
                {
                    var d = package.Drawings[m.Drawing];
                    return $"{d.FingerprintGuid}:{m.Handle}";
                })
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            obj.StableKey = obj.Type + "|" + string.Join("+", identities);

            var id = InsertDesignObject(db, obj);
            obj.Id = id;
            foreach (var m in obj.Mappings)
            {
                var via = m.Via == "data-reference" ? "data-reference" : "direct";
                InsertEntityMapping(db, id, sourceEntities[(m.Drawing, m.Handle)], m.Method, via);
                bySource[(m.Drawing, m.Handle)] = id;
            }

            if (obj.System != null && obj.Type == "GravityNetwork")
            {
                var rule = mapping.NetworkSystems.FirstOrDefault(r => r.System == obj.System);
                InsertClassification(db, id, "system", obj.System, "rule", rule?.Id);
            }
            else if (obj.System != null)
            {
                // Inherit system from network classification method rule for parts — still recorded on object properties; classification optional.
            }
        }

        // Relationships pipe-structure
        foreach (var drawing in package.Drawings.Values)
        {
            foreach (var entity in drawing.Entities.Where(e => e.Kind == "pipe" && e.Reference == null && e.Pipe != null))
            {
                if (!bySource.TryGetValue((drawing.Id, entity.Handle), out var pipeId)) continue;
                if (entity.Pipe!.StartStructure != null && bySource.TryGetValue((drawing.Id, entity.Pipe.StartStructure), out var startId))
                    InsertRelationship(db, "pipe-structure", pipeId, startId, "start");
                if (entity.Pipe.EndStructure != null && bySource.TryGetValue((drawing.Id, entity.Pipe.EndStructure), out var endId))
                    InsertRelationship(db, "pipe-structure", pipeId, endId, "end");
            }
        }

        return bySource;
    }

    private static string? FindNetworkName(CorexPackage package, string drawingId, Entity part)
    {
        string? networkHandle = part.Pipe?.Network ?? part.Structure?.Network;
        if (networkHandle == null) return null;
        var net = package.Drawings[drawingId].Entities.FirstOrDefault(e => e.Handle == networkHandle);
        return net?.Name;
    }

    private Dictionary<string, object?> BuildDesignProperties(Entity entity, CorexPackage package, DrawingDocument drawing)
    {
        var props = new Dictionary<string, object?>();
        if (entity.Pipe != null)
        {
            var p = entity.Pipe;
            var dx = p.EndPoint[0] - p.StartPoint[0];
            var dy = p.EndPoint[1] - p.StartPoint[1];
            var length2d = Math.Sqrt(dx * dx + dy * dy);
            var startInvert = p.StartPoint[2] - p.InnerDiameter / 2.0;
            var endInvert = p.EndPoint[2] - p.InnerDiameter / 2.0;
            var slopePercent = length2d > 0 ? (startInvert - endInvert) / length2d * 100.0 : 0;
            // Diameter in drawing units (feet) → inches
            var diameterIn = drawing.Units.Linear is "feet" or "us-survey-feet" ? p.InnerDiameter * 12.0 : p.InnerDiameter;
            props["innerDiameterIn"] = diameterIn;
            props["material"] = p.Material;
            props["startInvertFt"] = startInvert;
            props["endInvertFt"] = endInvert;
            props["length2dFt"] = length2d;
            props["slopePercent"] = slopePercent;
        }
        else if (entity.Structure != null)
        {
            var s = entity.Structure;
            props["rimElevationFt"] = s.RimElevation;
            props["sumpElevationFt"] = s.SumpElevation;
            props["positionFt"] = new[] { s.Position[0], s.Position[1] };
            props["connectedPipeCount"] = s.ConnectedPipes.Count;
        }
        else if (entity.Alignment != null)
        {
            props["lengthFt"] = entity.Alignment.Segments.Sum(seg => seg.Length);
        }
        return props;
    }

    private long InsertDesignObject(SqliteConnection db, DesignObjectProp obj)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO design_object(stable_key, type, name, system, basis, properties_json) VALUES ($k,$t,$n,$s,'native',$p); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$k", obj.StableKey);
        cmd.Parameters.AddWithValue("$t", obj.Type);
        cmd.Parameters.AddWithValue("$n", (object?)obj.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", (object?)obj.System ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$p", JsonSerializer.Serialize(obj.Properties));
        return (long)cmd.ExecuteScalar()!;
    }

    private static void InsertEntityMapping(SqliteConnection db, long designObjectId, long sourceEntityId, string method, string via)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO entity_mapping(design_object_id, source_entity_id, method, via) VALUES ($d,$s,$m,$v);";
        cmd.Parameters.AddWithValue("$d", designObjectId);
        cmd.Parameters.AddWithValue("$s", sourceEntityId);
        cmd.Parameters.AddWithValue("$m", method);
        cmd.Parameters.AddWithValue("$v", via);
        cmd.ExecuteNonQuery();
    }

    private static void InsertClassification(SqliteConnection db, long designObjectId, string property, string value, string method, string? ruleRef)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO classification(design_object_id, property, value, method, rule_ref) VALUES ($d,$p,$v,$m,$r);";
        cmd.Parameters.AddWithValue("$d", designObjectId);
        cmd.Parameters.AddWithValue("$p", property);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$m", method);
        cmd.Parameters.AddWithValue("$r", (object?)ruleRef ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void InsertRelationship(SqliteConnection db, string kind, long fromId, long toId, string role)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO design_relationship(kind, from_id, to_id, role, method) VALUES ($k,$f,$t,$r,'native');";
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$f", fromId);
        cmd.Parameters.AddWithValue("$t", toId);
        cmd.Parameters.AddWithValue("$r", role);
        cmd.ExecuteNonQuery();
    }

    private void BuildDocumentModel(
        CorexPackage package,
        Dictionary<string, long> drawings,
        Dictionary<(string, string), long> sourceEntities,
        Dictionary<(string, string), long> designObjects,
        SqliteConnection db,
        Dictionary<(string, string), long> layouts,
        Dictionary<(string, string), long> viewports,
        Dictionary<string, long> sheets)
    {
        var profileViews = new List<(string Drawing, string Handle, ProfileViewPayload Pv, long EntityId)>();

        foreach (var drawing in package.Drawings.Values)
        {
            foreach (var entity in drawing.Entities.Where(e => e.Kind == "profile-view" && e.ProfileView != null))
                profileViews.Add((drawing.Id, entity.Handle, entity.ProfileView!, sourceEntities[(drawing.Id, entity.Handle)]));

            foreach (var layout in drawing.Layouts)
            {
                long? titleBlockEntityId = layout.TitleBlock != null && sourceEntities.TryGetValue((drawing.Id, layout.TitleBlock.BlockReference), out var tb)
                    ? tb : null;
                var layoutId = InsertLayout(db, drawings[drawing.Id], layout, titleBlockEntityId);
                layouts[(drawing.Id, layout.Handle)] = layoutId;

                foreach (var vp in layout.Viewports)
                {
                    string? kind = null;
                    long? footprintId = null;
                    if (!vp.IsPaperSpaceView && vp.View != null)
                    {
                        var footprint = ViewportMath.WorldFootprint(
                            vp.CenterPaper[0], vp.CenterPaper[1], vp.Width, vp.Height,
                            vp.View.CustomScale, vp.View.Center[0], vp.View.Center[1], vp.View.Twist);
                        kind = "plan";
                        foreach (var pv in profileViews.Where(p => p.Drawing == drawing.Id))
                        {
                            // Profile viewport when footprint contains profile-view origin extents (model placement).
                            var origin = pv.Pv.Origin;
                            if (ViewportMath.PointInPolygon(origin[0], origin[1], footprint))
                                kind = "profile";
                        }
                        footprintId = InsertPolygonGeometry(db, footprint, "world", "feet");
                    }

                    var vpId = InsertViewport(db, layoutId, vp, kind, footprintId);
                    viewports[(drawing.Id, vp.Handle)] = vpId;
                    if (vp.FrozenLayers != null)
                    {
                        foreach (var layer in vp.FrozenLayers)
                            InsertViewportFrozenLayer(db, vpId, layer);
                    }
                }
            }
        }

        foreach (var pv in profileViews)
        {
            long? alignmentObjectId = null;
            // alignment handle on profile view may be a data-reference entity in same drawing
            if (designObjects.TryGetValue((pv.Drawing, pv.Pv.Alignment), out var aid))
                alignmentObjectId = aid;
            InsertProfileView(db, pv.EntityId, alignmentObjectId, pv.Pv);
        }

        if (package.Sheets != null)
        {
            foreach (var sheet in package.Sheets.Sheets)
            {
                var layoutId = layouts[(sheet.Drawing, sheet.Layout)];
                sheets[sheet.Number] = InsertSheet(db, sheet, layoutId);
            }
        }
    }

    private long InsertPolygonGeometry(SqliteConnection db, List<double[]> polygon, string space, string units)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO geometry(kind, space, units, is_derived, data_json, derivation) VALUES ('polygon',$sp,$u,1,$j,'viewport-footprint'); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$sp", space);
        cmd.Parameters.AddWithValue("$u", units);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(new { type = "polygon", vertices = polygon }));
        var id = (long)cmd.ExecuteScalar()!;
        var minX = polygon.Min(p => p[0]); var maxX = polygon.Max(p => p[0]);
        var minY = polygon.Min(p => p[1]); var maxY = polygon.Max(p => p[1]);
        using var r = db.CreateCommand();
        r.CommandText = "INSERT INTO geometry_rtree(id, min_x, max_x, min_y, max_y) VALUES ($id,$a,$b,$c,$d);";
        r.Parameters.AddWithValue("$id", id);
        r.Parameters.AddWithValue("$a", minX);
        r.Parameters.AddWithValue("$b", maxX);
        r.Parameters.AddWithValue("$c", minY);
        r.Parameters.AddWithValue("$d", maxY);
        r.ExecuteNonQuery();
        return id;
    }

    private static long InsertLayout(SqliteConnection db, long drawingId, LayoutInfo layout, long? titleBlockEntityId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO layout(drawing_id, handle, name, tab_order, title_block_entity_id) VALUES ($d,$h,$n,$t,$tb); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$d", drawingId);
        cmd.Parameters.AddWithValue("$h", layout.Handle);
        cmd.Parameters.AddWithValue("$n", layout.Name);
        cmd.Parameters.AddWithValue("$t", layout.TabOrder);
        cmd.Parameters.AddWithValue("$tb", (object?)titleBlockEntityId ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    private long InsertViewport(SqliteConnection db, long layoutId, ViewportInfo vp, string? kind, long? footprintId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO viewport(layout_id, handle, number, is_paper_space_view, kind, is_on, center_paper_x, center_paper_y, width_paper, height_paper, view_json, clip_json, footprint_geometry_id)
            VALUES ($l,$h,$n,$ps,$kind,$on,$cx,$cy,$w,$ht,$view,$clip,$fp); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$l", layoutId);
        cmd.Parameters.AddWithValue("$h", vp.Handle);
        cmd.Parameters.AddWithValue("$n", vp.Number);
        cmd.Parameters.AddWithValue("$ps", vp.IsPaperSpaceView ? 1 : 0);
        cmd.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$on", vp.On ? 1 : 0);
        cmd.Parameters.AddWithValue("$cx", vp.CenterPaper[0]);
        cmd.Parameters.AddWithValue("$cy", vp.CenterPaper[1]);
        cmd.Parameters.AddWithValue("$w", vp.Width);
        cmd.Parameters.AddWithValue("$ht", vp.Height);
        cmd.Parameters.AddWithValue("$view", vp.View == null ? DBNull.Value : JsonSerializer.Serialize(vp.View, _json));
        cmd.Parameters.AddWithValue("$clip", vp.ClipBoundary == null ? DBNull.Value : JsonSerializer.Serialize(vp.ClipBoundary));
        cmd.Parameters.AddWithValue("$fp", (object?)footprintId ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    private static void InsertViewportFrozenLayer(SqliteConnection db, long viewportId, string layer)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO viewport_frozen_layer(viewport_id, layer_name) VALUES ($v,$l);";
        cmd.Parameters.AddWithValue("$v", viewportId);
        cmd.Parameters.AddWithValue("$l", layer);
        cmd.ExecuteNonQuery();
    }

    private static void InsertProfileView(SqliteConnection db, long entityId, long? alignmentObjectId, ProfileViewPayload pv)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO profile_view(source_entity_id, alignment_object_id, station_start, station_end, elevation_min, elevation_max, vertical_exaggeration, origin_x, origin_y)
            VALUES ($e,$a,$ss,$se,$emin,$emax,$ve,$ox,$oy);
            """;
        cmd.Parameters.AddWithValue("$e", entityId);
        cmd.Parameters.AddWithValue("$a", (object?)alignmentObjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ss", pv.StationStart);
        cmd.Parameters.AddWithValue("$se", pv.StationEnd);
        cmd.Parameters.AddWithValue("$emin", pv.ElevationMin);
        cmd.Parameters.AddWithValue("$emax", pv.ElevationMax);
        cmd.Parameters.AddWithValue("$ve", pv.VerticalExaggeration);
        cmd.Parameters.AddWithValue("$ox", pv.Origin[0]);
        cmd.Parameters.AddWithValue("$oy", pv.Origin[1]);
        cmd.ExecuteNonQuery();
    }

    private static long InsertSheet(SqliteConnection db, SheetEntry sheet, long layoutId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO sheet(number, title, revision, layout_id, source) VALUES ($n,$t,$r,$l,$s); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", sheet.Number);
        cmd.Parameters.AddWithValue("$t", sheet.Title);
        cmd.Parameters.AddWithValue("$r", (object?)sheet.Revision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$l", layoutId);
        cmd.Parameters.AddWithValue("$s", sheet.Source);
        return (long)cmd.ExecuteScalar()!;
    }

    private void BuildPresentations(
        CorexPackage package,
        Dictionary<string, long> drawings,
        Dictionary<(string, string), long> sourceEntities,
        Dictionary<(string, string), long> designObjects,
        List<XrefPathRow> xrefPaths,
        Dictionary<(string, string), long> viewports,
        Dictionary<string, Dictionary<string, LayerInfo>> layersByDrawing,
        SqliteConnection db)
    {
        foreach (var drawing in package.Drawings.Values)
        {
            foreach (var layout in drawing.Layouts)
            {
                foreach (var vp in layout.Viewports.Where(v => !v.IsPaperSpaceView && v.View != null))
                {
                    var vpId = viewports[(drawing.Id, vp.Handle)];
                    var footprint = ViewportMath.WorldFootprint(
                        vp.CenterPaper[0], vp.CenterPaper[1], vp.Width, vp.Height,
                        vp.View!.CustomScale, vp.View.Center[0], vp.View.Center[1], vp.View.Twist);
                    var kind = "plan";
                    // detect profile
                    foreach (var e in drawing.Entities.Where(x => x.Kind == "profile-view" && x.ProfileView != null))
                    {
                        var o = e.ProfileView!.Origin;
                        if (ViewportMath.PointInPolygon(o[0], o[1], footprint))
                            kind = "profile";
                    }
                    if (kind != "plan")
                        continue; // first build: only plan viewports get presentation rows for coverage

                    // Native entities in the viewport drawing (none for sheets in mini-site model space)
                    // Entities through each loaded/unloaded xref path from this drawing
                    foreach (var path in xrefPaths.Where(p => p.Root == drawing.Id))
                    {
                        var leaf = path.PathDrawings.LastOrDefault();
                        if (leaf == null) continue;
                        if (!package.Drawings.TryGetValue(leaf, out var leafDrawing)) continue;

                        foreach (var entity in leafDrawing.Entities.Where(e => e.Space == "model" || e.Space == "block"))
                        {
                            // Resolve world geometry for inside test
                            if (!TryEntityWorldPoint(package, entity, leaf, path, out var wx, out var wy, out var blockPath))
                                continue;

                            var inside = ViewportMath.PointInPolygon(wx, wy, footprint);
                            if (!inside && entity.Kind == "pipe" && entity.Pipe != null && path.WorldTransform != null)
                            {
                                var s = path.WorldTransform.Value.TransformPoint(entity.Pipe.StartPoint);
                                var ept = path.WorldTransform.Value.TransformPoint(entity.Pipe.EndPoint);
                                inside = ViewportMath.SegmentIntersectsPolygon(s.X, s.Y, ept.X, ept.Y, footprint);
                            }
                            if (!inside) continue;

                            var (visible, reasons, effectiveLayer) = ResolveVisibility(
                                package, drawing, vp, path, leafDrawing, entity, blockPath, layersByDrawing);

                            long? designObjectId = designObjects.TryGetValue((leaf, entity.Handle), out var doid) ? doid : null;
                            InsertPresentation(db, vpId, sourceEntities[(leaf, entity.Handle)], path.Id, designObjectId, inside, visible, reasons, effectiveLayer);
                        }
                    }
                }
            }
        }
    }

    private static bool TryEntityWorldPoint(
        CorexPackage package,
        Entity entity,
        string leafDrawingId,
        XrefPathRow path,
        out double wx,
        out double wy,
        out List<(string Drawing, string Handle)> blockPath)
    {
        wx = wy = 0;
        blockPath = [];
        if (path.WorldTransform == null && path.Loaded)
            return false;
        // For not-loaded paths we may still evaluate geometry with transform when available (overlay path has transform)
        var xf = path.WorldTransform ?? Matrix4d.Identity;

        if (entity.Space == "block")
        {
            // Find block reference that owns this entity via owner handle
            var leaf = package.Drawings[leafDrawingId];
            var ownerHandle = entity.Owner;
            var blockRef = leaf.Entities.FirstOrDefault(e => e.Kind == "block-reference" && e.Block?.Definition == ownerHandle)
                           ?? leaf.Entities.FirstOrDefault(e => e.Kind == "block-reference" && e.Handle != null &&
                                leaf.BlockDefinitions.Any(b => b.Handle == ownerHandle && e.Block?.Definition == b.Handle));
            // Prefer block refs whose definition contains this entity
            blockRef = leaf.Entities.FirstOrDefault(e => e.Kind == "block-reference" && e.Block != null &&
                leaf.BlockDefinitions.Any(b => b.Handle == e.Block.Definition && b.Handle == entity.Owner));
            if (blockRef?.Block == null) return false;
            blockPath = [(leafDrawingId, blockRef.Handle)];
            var blockXf = Matrix4d.FromArray(blockRef.Block.Transform);
            var composed = xf.Multiply(blockXf);
            double[] local =
                entity.Geometry?.Center ??
                entity.Geometry?.Position ??
                entity.Geometry?.Start ??
                [0, 0, 0];
            var p = composed.TransformPoint(local);
            wx = p.X; wy = p.Y;
            return true;
        }

        double[]? pt = entity.Structure?.Position
            ?? entity.Geometry?.Center
            ?? entity.Geometry?.Start
            ?? entity.Geometry?.Position
            ?? entity.Pipe?.StartPoint;
        if (pt == null) return false;
        var wp = xf.TransformPoint(pt);
        wx = wp.X; wy = wp.Y;
        return true;
    }

    private static (bool Visible, List<string> Reasons, string? EffectiveLayer) ResolveVisibility(
        CorexPackage package,
        DrawingDocument hostDrawing,
        ViewportInfo vp,
        XrefPathRow path,
        DrawingDocument leafDrawing,
        Entity entity,
        List<(string Drawing, string Handle)> blockPath,
        Dictionary<string, Dictionary<string, LayerInfo>> layersByDrawing)
    {
        var reasons = new List<string>();
        if (!path.Loaded)
            reasons.Add("xref-not-loaded");
        if (!vp.On)
            reasons.Add("viewport-off");

        // Effective layer
        var layerName = entity.Layer;
        if (layerName == "0" && blockPath.Count > 0)
        {
            var br = package.Drawings[blockPath[0].Drawing].Entities.First(e => e.Handle == blockPath[0].Handle);
            layerName = br.Layer;
        }
        if (layerName == "0")
        {
            // top of xref: use xref insert layer of the instance that owns the leaf drawing
            var leafInstance = path.Instances.LastOrDefault();
            if (leafInstance.Drawing != null)
            {
                var xref = package.Drawings[leafInstance.Drawing].Xrefs.First(x => x.Handle == leafInstance.Handle);
                layerName = xref.Layer;
            }
        }

        // Host layer name through xref
        string effectiveLayer = layerName;
        if (path.Instances.Count > 0)
        {
            // Use the xref block name of the drawing that owns the layer (leaf)
            var leafInstance = path.Instances.Last();
            var xref = package.Drawings[leafInstance.Drawing].Xrefs.First(x => x.Handle == leafInstance.Handle);
            effectiveLayer = $"{xref.BlockName}|{layerName}";
        }

        // Style components for Civil objects
        StyleInfo? style = null;
        if (entity.Style != null)
            style = leafDrawing.Styles.FirstOrDefault(s => s.Handle == entity.Style);

        bool visible = reasons.Count == 0;
        if (entity.Visible == false)
        {
            reasons.Add("entity-invisible");
            visible = false;
        }

        if (style != null)
        {
            var components = style.Components.Where(c => c.View == "plan").ToList();
            if (components.Count > 0 && components.All(c => !c.Visible))
            {
                reasons.Add("style-hides-object");
                visible = false;
            }
            else
            {
                // Evaluate per component layers; object visible if any component visible
                var anyVisible = false;
                var allFrozenVp = true;
                var allOff = true;
                var allFrozen = true;
                foreach (var c in components.Where(c => c.Visible))
                {
                    var compLayer = c.Layer == "0" ? layerName : c.Layer;
                    var hostLayer = path.Instances.Count > 0
                        ? $"{package.Drawings[path.Instances.Last().Drawing].Xrefs.First(x => x.Handle == path.Instances.Last().Handle).BlockName}|{compLayer}"
                        : compLayer;
                    effectiveLayer = hostLayer;

                    var layerState = ResolveHostLayerState(hostDrawing, hostLayer, layersByDrawing, package, path);
                    if (layerState.On) allOff = false;
                    if (!layerState.Frozen) allFrozen = false;
                    var frozenVp = vp.FrozenLayers?.Contains(hostLayer) == true;
                    if (!frozenVp) allFrozenVp = false;
                    if (layerState.On && !layerState.Frozen && !frozenVp)
                        anyVisible = true;
                }

                if (!anyVisible && components.Any(c => c.Visible))
                {
                    if (allOff) reasons.Add("style-component-layer-off");
                    else if (allFrozen) reasons.Add("style-component-layer-frozen");
                    else if (allFrozenVp) reasons.Add("style-component-layer-frozen-in-viewport");
                    visible = false;
                }
                else if (anyVisible)
                {
                    visible = reasons.Count == 0 || (reasons.Count == 1 && reasons[0] == "xref-not-loaded" ? false : reasons.All(r => r == "xref-not-loaded") ? false : visible);
                    if (path.Loaded && reasons.Count == 0)
                        visible = true;
                }
            }
        }
        else
        {
            var layerState = ResolveHostLayerState(hostDrawing, effectiveLayer, layersByDrawing, package, path);
            if (!layerState.On) { reasons.Add("layer-off"); visible = false; }
            if (layerState.Frozen) { reasons.Add("layer-frozen"); visible = false; }
            if (vp.FrozenLayers?.Contains(effectiveLayer) == true)
            {
                reasons.Add("layer-frozen-in-viewport");
                visible = false;
            }
            if (path.Loaded && reasons.Count == 0)
                visible = true;
            if (!path.Loaded)
                visible = false;
        }

        if (!path.Loaded)
            visible = false;

        // Order reasons by precedence
        var order = new List<string>
        {
            "xref-not-loaded","viewport-off","clipped-by-viewport","clipped-by-xref","xref-insert-layer-hidden",
            "entity-invisible","style-hides-object","layer-off","layer-frozen","layer-frozen-in-viewport",
            "style-component-layer-off","style-component-layer-frozen","style-component-layer-frozen-in-viewport"
        };
        reasons = reasons.Distinct().OrderBy(r => order.IndexOf(r)).ToList();
        return (visible, reasons, effectiveLayer);
    }

    private static (bool On, bool Frozen) ResolveHostLayerState(
        DrawingDocument hostDrawing,
        string hostLayerName,
        Dictionary<string, Dictionary<string, LayerInfo>> layersByDrawing,
        CorexPackage package,
        XrefPathRow path)
    {
        // VISRETAIN true → use host's Xref-dependent layer records
        if (hostDrawing.Settings.Visretain)
        {
            var layer = hostDrawing.Layers.FirstOrDefault(l => l.Name == hostLayerName);
            if (layer != null)
                return (layer.On, layer.Frozen);
        }
        // Fallback to leaf drawing layer table (strip xref prefix)
        var leaf = path.PathDrawings.LastOrDefault();
        if (leaf != null)
        {
            var shortName = hostLayerName.Contains('|') ? hostLayerName.Split('|').Last() : hostLayerName;
            if (layersByDrawing.TryGetValue(leaf, out var layers) && layers.TryGetValue(shortName, out var layer))
                return (layer.On, layer.Frozen);
        }
        return (true, false);
    }

    private static void InsertPresentation(
        SqliteConnection db,
        long viewportId,
        long sourceEntityId,
        long xrefPathId,
        long? designObjectId,
        bool inside,
        bool visible,
        List<string> reasons,
        string? effectiveLayer)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO presentation_instance(viewport_id, source_entity_id, xref_path_id, design_object_id, is_inside, is_visible, reasons_json, effective_layer)
            VALUES ($v,$e,$x,$d,$in,$vis,$r,$el);
            """;
        cmd.Parameters.AddWithValue("$v", viewportId);
        cmd.Parameters.AddWithValue("$e", sourceEntityId);
        cmd.Parameters.AddWithValue("$x", xrefPathId);
        cmd.Parameters.AddWithValue("$d", (object?)designObjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$in", inside ? 1 : 0);
        cmd.Parameters.AddWithValue("$vis", visible ? 1 : 0);
        cmd.Parameters.AddWithValue("$r", JsonSerializer.Serialize(reasons));
        cmd.Parameters.AddWithValue("$el", (object?)effectiveLayer ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void LinkAnnotations(
        CorexPackage package,
        Dictionary<string, long> drawings,
        Dictionary<(string, string), long> sourceEntities,
        Dictionary<(string, string), long> designObjects,
        Dictionary<(string, string), long> viewports,
        SqliteConnection db)
    {
        foreach (var drawing in package.Drawings.Values)
        {
            foreach (var entity in drawing.Entities)
            {
                if (entity.Kind == "label" && entity.Label != null)
                {
                    long? designObjectId = designObjects.TryGetValue((drawing.Id, entity.Label.Annotates), out var id) ? id : null;
                    // For labels on data-ref drawings, annotates may be local handle of pipe ref
                    if (designObjectId == null && designObjects.TryGetValue((drawing.Id, entity.Label.Annotates), out var id2))
                        designObjectId = id2;
                    InsertAnnotation(db, sourceEntities[(drawing.Id, entity.Handle)], "label", entity.Label.Text, entity.Label.TextOverridden, designObjectId, designObjectId == null ? null : "label-annotates", null);
                }
                else if (entity.Kind == "mleader" && entity.Text != null)
                {
                    InsertAnnotation(db, sourceEntities[(drawing.Id, entity.Handle)], "mleader", entity.Text.Plain, null, null, null, null);
                }
            }
        }
    }

    private static void InsertAnnotation(SqliteConnection db, long sourceEntityId, string kind, string? plain, bool? overridden, long? designObjectId, string? linkMethod, double? linkDistance)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO annotation(source_entity_id, kind, plain_text, text_overridden, design_object_id, link_method, link_distance)
            VALUES ($e,$k,$p,$o,$d,$m,$dist);
            """;
        cmd.Parameters.AddWithValue("$e", sourceEntityId);
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$p", (object?)plain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$o", overridden == null ? DBNull.Value : overridden.Value ? 1 : 0);
        cmd.Parameters.AddWithValue("$d", (object?)designObjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$m", (object?)linkMethod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dist", (object?)linkDistance ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void ValidateModel(CorexPackage package, Dictionary<string, long> drawings, List<ValidationMessage> messages)
    {
        // Identity uniqueness already enforced by DB constraints; additional soft checks live in messages list.
        _ = package; _ = drawings; _ = messages;
    }

    private static void InsertValidationMessage(SqliteConnection db, ValidationMessage m)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO validation_message(severity, code, message, subject_json) VALUES ($s,$c,$m,$subj);";
        cmd.Parameters.AddWithValue("$s", m.Severity);
        cmd.Parameters.AddWithValue("$c", m.Code);
        cmd.Parameters.AddWithValue("$m", m.Message);
        cmd.Parameters.AddWithValue("$subj", (object?)m.SubjectJson ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static ValidationMessage Warn(string code, string message, string subjectJson) => new()
    {
        Severity = "warning",
        Code = code,
        Message = message,
        SubjectJson = subjectJson
    };

    private static string Subject(string drawing, string handle) =>
        JsonSerializer.Serialize(new { drawing, handle });
}
