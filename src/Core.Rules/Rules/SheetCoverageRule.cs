using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Rules.Engine;

namespace Core.Rules.Rules;

public sealed class SheetCoverageRule : IRule
{
    public string Id => "core.sheet-coverage";
    public int Version => 1;
    public string Kind => "deterministic";
    public string SpecRef => "docs/rules/RULE-003-sheet-coverage.md";

    public void Execute(RuleContext context)
    {
        var objectTypes = context.Params["objectTypes"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
        // useViewFrames available for later; mini-site has no view frames so use viewport footprints.

        using var sheets = context.Core.Connection.CreateCommand();
        sheets.CommandText = """
            SELECT s.id, s.number, l.drawing_id, d.corex_id
            FROM sheet s
            JOIN layout l ON l.id = s.layout_id
            JOIN drawing d ON d.id = l.drawing_id
            ORDER BY s.number;
            """;
        using var sheetReader = sheets.ExecuteReader();
        var sheetList = new List<(long Id, string Number, long DrawingId, string DrawingCorex)>();
        while (sheetReader.Read())
            sheetList.Add((sheetReader.GetInt64(0), sheetReader.GetString(1), sheetReader.GetInt64(2), sheetReader.GetString(3)));

        foreach (var sheet in sheetList)
        {
            using var vpCmd = context.Core.Connection.CreateCommand();
            vpCmd.CommandText = """
                SELECT v.id, v.handle, v.kind, g.data_json
                FROM viewport v
                JOIN layout l ON l.id = v.layout_id
                LEFT JOIN geometry g ON g.id = v.footprint_geometry_id
                WHERE l.id = (SELECT layout_id FROM sheet WHERE id = $sid)
                  AND v.is_paper_space_view = 0 AND v.kind = 'plan';
                """;
            vpCmd.Parameters.AddWithValue("$sid", sheet.Id);
            var viewports = new List<(long Id, string Handle, JsonArray? Footprint)>();
            using (var vr = vpCmd.ExecuteReader())
            {
                while (vr.Read())
                {
                    JsonArray? fp = null;
                    if (!vr.IsDBNull(3))
                    {
                        var data = JsonNode.Parse(vr.GetString(3))!;
                        fp = data["vertices"]!.AsArray();
                    }
                    viewports.Add((vr.GetInt64(0), vr.GetString(1), fp));
                }
            }
            if (viewports.Count == 0) continue;

            using var objCmd = context.Core.Connection.CreateCommand();
            objCmd.CommandText = """
                SELECT do.id, do.type, do.name, d.corex_id, se.handle, do.properties_json
                FROM design_object do
                JOIN entity_mapping em ON em.design_object_id = do.id AND em.via = 'direct'
                JOIN source_entity se ON se.id = em.source_entity_id
                JOIN drawing d ON d.id = se.drawing_id
                WHERE do.type IN (SELECT value FROM json_each($types));
                """;
            // simpler: load all and filter
            objCmd.CommandText = """
                SELECT do.id, do.type, do.name, d.corex_id, se.handle, do.properties_json
                FROM design_object do
                JOIN entity_mapping em ON em.design_object_id = do.id AND em.via = 'direct'
                JOIN source_entity se ON se.id = em.source_entity_id
                JOIN drawing d ON d.id = se.drawing_id;
                """;
            using var or = objCmd.ExecuteReader();
            while (or.Read())
            {
                var type = or.GetString(1);
                if (!objectTypes.Contains(type)) continue;
                var doId = or.GetInt64(0);
                var name = or.IsDBNull(2) ? "" : or.GetString(2);
                var drawing = or.GetString(3);
                var handle = or.GetString(4);
                var props = JsonNode.Parse(or.GetString(5))!.AsObject();

                // Candidate if inside any plan viewport footprint
                var vpResults = new JsonArray();
                var insideAny = false;
                var visibleAny = false;
                foreach (var vp in viewports)
                {
                    using var pcmd = context.Core.Connection.CreateCommand();
                    pcmd.CommandText = """
                        SELECT is_inside, is_visible, reasons_json, effective_layer
                        FROM presentation_instance
                        WHERE viewport_id = $v AND design_object_id = $d
                        LIMIT 1;
                        """;
                    pcmd.Parameters.AddWithValue("$v", vp.Id);
                    pcmd.Parameters.AddWithValue("$d", doId);
                    using var pr = pcmd.ExecuteReader();
                    bool inside = false, visible = false;
                    JsonNode reasons = new JsonArray();
                    string? effectiveLayer = null;
                    if (pr.Read())
                    {
                        inside = pr.GetInt64(0) == 1;
                        visible = pr.GetInt64(1) == 1;
                        reasons = JsonNode.Parse(pr.GetString(2))!;
                        effectiveLayer = pr.IsDBNull(3) ? null : pr.GetString(3);
                    }
                    else if (vp.Footprint != null)
                    {
                        // fallback geometry test for structures/pipes without presentation (shouldn't happen for in-footprint)
                        inside = false;
                    }

                    if (inside) insideAny = true;
                    if (visible) visibleAny = true;
                    if (inside)
                    {
                        vpResults.Add(new JsonObject
                        {
                            ["viewport"] = new JsonObject { ["drawing"] = sheet.DrawingCorex, ["handle"] = vp.Handle },
                            ["inside"] = inside,
                            ["visible"] = visible,
                            ["reasons"] = reasons.DeepClone(),
                            ["effectiveLayer"] = effectiveLayer
                        });
                    }
                }

                if (!insideAny) continue; // not a candidate for this sheet's viewport coverage
                if (visibleAny) continue;

                var subjectKey = $"{drawing}:{handle}";
                context.EmitFinding(new FindingDraft
                {
                    FindingKey = $"{Id}|object-not-visible-on-sheet|{subjectKey}|{sheet.Number}",
                    Code = "object-not-visible-on-sheet",
                    Severity = "medium",
                    Basis = "native",
                    SubjectKey = subjectKey,
                    SheetNumber = sheet.Number,
                    Title = $"{name} not visible on sheet {sheet.Number}",
                    FactsJson = new JsonObject
                    {
                        ["sheet"] = sheet.Number,
                        ["designObject"] = name,
                        ["viewports"] = vpResults
                    }.ToJsonString()
                });
            }
        }
    }
}
