using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Core.Rules.Engine;

namespace Core.Rules.Rules;

public sealed class AnnotationVsModelRule : IRule
{
    public string Id => "core.annotation-vs-model";
    public int Version => 1;
    public string Kind => "deterministic";
    public string SpecRef => "docs/rules/RULE-004-annotation-vs-model.md";

    private static readonly Regex Callout = new(
        @"^(?:(?<length>\d+(?:\.\d+)?)\s*(?:LF|')\s*(?:OF\s+)?)?(?<diameter>\d+(?:\.\d+)?)\s*(?:""|IN|INCH)\s+(?<material>[A-Za-z0-9]+)\s*(?:(?:@|AT|S=)\s*(?<slope>\d+(?:\.\d+)?)\s*%?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public void Execute(RuleContext context)
    {
        var materialCodes = context.Params["materialCodes"]!.AsObject()
            .ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        var diameterTol = context.Params["diameterToleranceIn"]!.GetValue<double>();
        var lengthTol = context.Params["lengthToleranceFt"]!.GetValue<double>();
        var linkTolPaper = context.Params["linkTolerancePaper"]!.GetValue<double>();
        var linkTolModel = context.Params["linkToleranceModelFt"]!.GetValue<double>();

        // Labels
        using (var cmd = context.Core.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT d.corex_id, se.handle, a.plain_text, a.text_overridden, a.design_object_id, do.name, do.properties_json
                FROM annotation a
                JOIN source_entity se ON se.id = a.source_entity_id
                JOIN drawing d ON d.id = se.drawing_id
                LEFT JOIN design_object do ON do.id = a.design_object_id
                WHERE a.kind = 'label';
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(2) || r.IsDBNull(3) || r.GetInt64(3) != 1) continue; // only overridden
                var text = r.GetString(2);
                var parsed = ParseCallout(text);
                if (parsed is null) continue;
                if (r.IsDBNull(4)) continue;
                var drawing = r.GetString(0);
                var handle = r.GetString(1);
                var pipeName = r.IsDBNull(5) ? "" : r.GetString(5);
                var props = JsonNode.Parse(r.GetString(6))!.AsObject();
                EmitMismatches(context, drawing, handle, text, pipeName, "label-annotates", true, "native", parsed, props, materialCodes, diameterTol, lengthTol);
            }
        }

        // MLeaders — link by proximity through plan viewports
        using (var cmd = context.Core.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT d.corex_id, se.handle, a.plain_text, se.payload_json, se.space
                FROM annotation a
                JOIN source_entity se ON se.id = a.source_entity_id
                JOIN drawing d ON d.id = se.drawing_id
                WHERE a.kind = 'mleader';
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var drawing = r.GetString(0);
                var handle = r.GetString(1);
                var text = r.IsDBNull(2) ? null : r.GetString(2);
                if (text is null) continue;
                var parsed = ParseCallout(text);
                if (parsed is null) continue;
                var payload = r.IsDBNull(3) ? null : JsonNode.Parse(r.GetString(3));
                var space = r.GetString(4);
                var leader = payload?["leader"]?["vertices"]?.AsArray();
                if (leader is null || leader.Count == 0) continue;
                var tip = leader[0]!.AsArray();
                double tipX = tip[0]!.GetValue<double>(), tipY = tip[1]!.GetValue<double>();

                // Map paper tip through plan viewport to world
                double worldX = tipX, worldY = tipY;
                double tolerance = linkTolModel;
                if (space == "paper")
                {
                    using var vpCmd = context.Core.Connection.CreateCommand();
                    vpCmd.CommandText = """
                        SELECT v.center_paper_x, v.center_paper_y, v.width_paper, v.height_paper, v.view_json
                        FROM viewport v
                        JOIN layout l ON l.id = v.layout_id
                        JOIN drawing d ON d.id = l.drawing_id
                        WHERE d.corex_id = $d AND v.is_paper_space_view = 0 AND v.kind = 'plan'
                        LIMIT 1;
                        """;
                    vpCmd.Parameters.AddWithValue("$d", drawing);
                    using var vr = vpCmd.ExecuteReader();
                    if (!vr.Read()) continue;
                    var cx = vr.GetDouble(0); var cy = vr.GetDouble(1);
                    var view = JsonNode.Parse(vr.GetString(4))!;
                    var scale = view["customScale"]!.GetValue<double>();
                    var vc = view["center"]!.AsArray();
                    worldX = (tipX - cx) / scale + vc[0]!.GetValue<double>();
                    worldY = (tipY - cy) / scale + vc[1]!.GetValue<double>();
                    tolerance = linkTolPaper / scale;
                }

                var candidates = FindPipesNear(context, worldX, worldY, tolerance);
                var subjectKey = $"{drawing}:{handle}";
                if (candidates.Count == 0)
                {
                    context.EmitObservation(new ObservationDraft
                    {
                        Code = "callout-unlinked",
                        Basis = "rule",
                        SubjectKey = subjectKey,
                        FactsJson = new JsonObject { ["text"] = text }.ToJsonString()
                    });
                    continue;
                }
                if (candidates.Count > 1)
                {
                    context.EmitObservation(new ObservationDraft
                    {
                        Code = "callout-ambiguous",
                        Basis = "rule",
                        SubjectKey = subjectKey,
                        FactsJson = new JsonObject { ["text"] = text, ["count"] = candidates.Count }.ToJsonString()
                    });
                    continue;
                }

                var pipe = candidates[0];
                EmitMismatches(context, drawing, handle, text, pipe.Name, "leader-proximity", false, "rule", parsed, pipe.Props, materialCodes, diameterTol, lengthTol);
            }
        }
    }

    private sealed record PipeCand(string Name, JsonObject Props, double Dist);

    private static List<PipeCand> FindPipesNear(RuleContext context, double x, double y, double tol)
    {
        var list = new List<PipeCand>();
        using var cmd = context.Core.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT do.name, do.properties_json, se.payload_json
            FROM design_object do
            JOIN entity_mapping em ON em.design_object_id = do.id AND em.via = 'direct'
            JOIN source_entity se ON se.id = em.source_entity_id
            WHERE do.type = 'GravityPipe';
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var name = r.IsDBNull(0) ? "" : r.GetString(0);
            var props = JsonNode.Parse(r.GetString(1))!.AsObject();
            var payload = JsonNode.Parse(r.GetString(2))!;
            var s = payload["startPoint"]!.AsArray();
            var e = payload["endPoint"]!.AsArray();
            var dist = DistPointToSegment(x, y, s[0]!.GetValue<double>(), s[1]!.GetValue<double>(), e[0]!.GetValue<double>(), e[1]!.GetValue<double>());
            if (dist <= tol)
                list.Add(new PipeCand(name, props, dist));
        }
        return list.OrderBy(p => p.Dist).ToList();
    }

    private static double DistPointToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1; var dy = y2 - y1;
        if (dx == 0 && dy == 0) return Math.Sqrt((px - x1)*(px - x1) + (py - y1)*(py - y1));
        var t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy), 0, 1);
        var qx = x1 + t * dx; var qy = y1 + t * dy;
        return Math.Sqrt((px - qx)*(px - qx) + (py - qy)*(py - qy));
    }

    private sealed record ParsedCallout(double? Length, double Diameter, string Material, double? Slope, int SlopeDecimals);

    private static ParsedCallout? ParseCallout(string text)
    {
        var m = Callout.Match(text.Trim());
        if (!m.Success) return null;
        double? length = m.Groups["length"].Success ? double.Parse(m.Groups["length"].Value, CultureInfo.InvariantCulture) : null;
        var diameter = double.Parse(m.Groups["diameter"].Value, CultureInfo.InvariantCulture);
        var material = m.Groups["material"].Value;
        double? slope = null;
        var slopeDecimals = 0;
        if (m.Groups["slope"].Success)
        {
            var raw = m.Groups["slope"].Value;
            slope = double.Parse(raw, CultureInfo.InvariantCulture);
            var dot = raw.IndexOf('.');
            slopeDecimals = dot < 0 ? 0 : raw.Length - dot - 1;
        }
        return new ParsedCallout(length, diameter, material, slope, slopeDecimals);
    }

    private static void EmitMismatches(
        RuleContext context, string drawing, string handle, string text, string pipeName, string linkMethod,
        bool textOverridden, string basis, ParsedCallout parsed, JsonObject props,
        Dictionary<string, string> materialCodes, double diameterTol, double lengthTol)
    {
        var mismatches = new JsonArray();
        var modelDiameter = props["innerDiameterIn"]!.GetValue<double>();
        if (Math.Abs(parsed.Diameter - modelDiameter) > diameterTol)
        {
            mismatches.Add(new JsonObject
            {
                ["field"] = "diameter",
                ["stated"] = parsed.Diameter,
                ["model"] = modelDiameter,
                ["units"] = "in"
            });
        }

        var modelMaterialRaw = props["material"]!.GetValue<string>();
        if (!materialCodes.TryGetValue(modelMaterialRaw, out var modelCode))
        {
            context.EmitObservation(new ObservationDraft
            {
                Code = "material-unmapped",
                Basis = basis,
                SubjectKey = $"{drawing}:{handle}",
                FactsJson = new JsonObject { ["material"] = modelMaterialRaw }.ToJsonString()
            });
        }
        else if (!string.Equals(modelCode, parsed.Material, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add(new JsonObject
            {
                ["field"] = "material",
                ["stated"] = parsed.Material,
                ["model"] = modelCode,
                ["modelRaw"] = modelMaterialRaw
            });
        }

        if (parsed.Slope is double statedSlope)
        {
            var modelSlope = props["slopePercent"]!.GetValue<double>();
            var tol = 0.5 * Math.Pow(10, -parsed.SlopeDecimals);
            if (Math.Abs(statedSlope - modelSlope) > tol)
            {
                mismatches.Add(new JsonObject
                {
                    ["field"] = "slope",
                    ["stated"] = statedSlope,
                    ["model"] = Math.Round(modelSlope, 4),
                    ["units"] = "%",
                    ["tolerance"] = tol
                });
            }
        }

        if (parsed.Length is double statedLength)
        {
            var modelLength = props["length2dFt"]!.GetValue<double>();
            if (Math.Abs(statedLength - modelLength) > lengthTol)
            {
                mismatches.Add(new JsonObject
                {
                    ["field"] = "length",
                    ["stated"] = statedLength,
                    ["model"] = modelLength,
                    ["units"] = "ft"
                });
            }
        }

        if (mismatches.Count == 0) return;

        var severity = mismatches.Any(m => m!["field"]!.GetValue<string>() is "diameter" or "material") ? "high" : "medium";
        var facts = new JsonObject
        {
            ["text"] = text,
            ["linkedTo"] = pipeName,
            ["linkMethod"] = linkMethod,
            ["mismatches"] = mismatches
        };
        if (textOverridden) facts["textOverridden"] = true;

        context.EmitFinding(new FindingDraft
        {
            FindingKey = $"core.annotation-vs-model|annotation-value-mismatch|{drawing}:{handle}",
            Code = "annotation-value-mismatch",
            Severity = severity,
            Basis = basis,
            SubjectKey = $"{drawing}:{handle}",
            Title = $"Annotation mismatch: {text}",
            FactsJson = facts.ToJsonString()
        });
    }
}
