using System.Text.Json.Nodes;
using Core.Rules.Engine;

namespace Core.Rules.Rules;

public sealed class NetworkConnectivityRule : IRule
{
    public string Id => "core.network-connectivity";
    public int Version => 1;
    public string Kind => "deterministic";
    public string SpecRef => "docs/rules/RULE-002-network-connectivity.md";

    public void Execute(RuleContext context)
    {
        var reportStructures = context.Params["reportStructuresWithoutPipes"]!.GetValue<bool>();

        // Evaluate each DesignObject once via primary (direct) mapping.
        using var pipes = context.Core.Connection.CreateCommand();
        pipes.CommandText = """
            SELECT do.id, do.name, do.properties_json, d.corex_id, se.handle, se.payload_json
            FROM design_object do
            JOIN entity_mapping em ON em.design_object_id = do.id AND em.via = 'direct'
            JOIN source_entity se ON se.id = em.source_entity_id
            JOIN drawing d ON d.id = se.drawing_id
            WHERE do.type = 'GravityPipe'
            ORDER BY do.name;
            """;
        using (var r = pipes.ExecuteReader())
        {
            while (r.Read())
            {
                var name = r.IsDBNull(1) ? "" : r.GetString(1);
                var drawing = r.GetString(3);
                var handle = r.GetString(4);
                var payload = JsonNode.Parse(r.GetString(5))!;
                var subjectKey = $"{drawing}:{handle}";

                string? start = payload["startStructure"]?.GetValue<string?>();
                string? end = payload["endStructure"]?.GetValue<string?>();
                var startPt = payload["startPoint"]!.AsArray();
                var endPt = payload["endPoint"]!.AsArray();

                if (start is null)
                {
                    context.EmitFinding(new FindingDraft
                    {
                        FindingKey = $"{Id}|pipe-end-unconnected|{subjectKey}|start",
                        Code = "pipe-end-unconnected",
                        Severity = "medium",
                        Basis = "native",
                        SubjectKey = subjectKey,
                        Title = $"Pipe {name} start unconnected",
                        FactsJson = new JsonObject
                        {
                            ["pipe"] = name,
                            ["end"] = "start",
                            ["point"] = new JsonArray(startPt[0]!.GetValue<double>(), startPt[1]!.GetValue<double>())
                        }.ToJsonString()
                    });
                }
                if (end is null)
                {
                    context.EmitFinding(new FindingDraft
                    {
                        FindingKey = $"{Id}|pipe-end-unconnected|{subjectKey}|end",
                        Code = "pipe-end-unconnected",
                        Severity = "medium",
                        Basis = "native",
                        SubjectKey = subjectKey,
                        Title = $"Pipe {name} end unconnected",
                        FactsJson = new JsonObject
                        {
                            ["pipe"] = name,
                            ["end"] = "end",
                            ["point"] = new JsonArray(endPt[0]!.GetValue<double>(), endPt[1]!.GetValue<double>())
                        }.ToJsonString()
                    });
                }

                // connection-mismatch checks
                CheckMismatch(context, Id, drawing, handle, name, start, end, payload);
            }
        }

        if (reportStructures)
        {
            using var structs = context.Core.Connection.CreateCommand();
            structs.CommandText = """
                SELECT do.name, d.corex_id, se.handle, se.payload_json
                FROM design_object do
                JOIN entity_mapping em ON em.design_object_id = do.id AND em.via = 'direct'
                JOIN source_entity se ON se.id = em.source_entity_id
                JOIN drawing d ON d.id = se.drawing_id
                WHERE do.type = 'GravityStructure'
                ORDER BY do.name;
                """;
            using var r = structs.ExecuteReader();
            while (r.Read())
            {
                var name = r.IsDBNull(0) ? "" : r.GetString(0);
                var drawing = r.GetString(1);
                var handle = r.GetString(2);
                var payload = JsonNode.Parse(r.GetString(3))!;
                var connected = payload["connectedPipes"]!.AsArray();
                if (connected.Count == 0)
                {
                    var subjectKey = $"{drawing}:{handle}";
                    context.EmitFinding(new FindingDraft
                    {
                        FindingKey = $"{Id}|structure-without-pipes|{subjectKey}",
                        Code = "structure-without-pipes",
                        Severity = "medium",
                        Basis = "native",
                        SubjectKey = subjectKey,
                        Title = $"Structure {name} has no pipes",
                        FactsJson = new JsonObject { ["structure"] = name }.ToJsonString()
                    });
                }
            }
        }
    }

    private static void CheckMismatch(RuleContext context, string ruleId, string drawing, string handle, string pipeName, string? start, string? end, JsonNode payload)
    {
        foreach (var (role, structHandle) in new[] { ("start", start), ("end", end) })
        {
            if (structHandle is null) continue;
            using var cmd = context.Core.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT payload_json FROM source_entity se
                JOIN drawing d ON d.id = se.drawing_id
                WHERE d.corex_id = $d AND se.handle = $h;
                """;
            cmd.Parameters.AddWithValue("$d", drawing);
            cmd.Parameters.AddWithValue("$h", structHandle);
            var structPayload = cmd.ExecuteScalar() as string;
            if (structPayload is null) continue;
            var pipes = JsonNode.Parse(structPayload)!["connectedPipes"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet();
            if (!pipes.Contains(handle))
            {
                var subjectKey = $"{drawing}:{handle}";
                context.EmitFinding(new FindingDraft
                {
                    FindingKey = $"{ruleId}|connection-mismatch|{subjectKey}|{role}",
                    Code = "connection-mismatch",
                    Severity = "high",
                    Basis = "native",
                    SubjectKey = subjectKey,
                    Title = $"Connection mismatch for pipe {pipeName}",
                    FactsJson = new JsonObject
                    {
                        ["pipe"] = pipeName,
                        ["end"] = role,
                        ["structure"] = structHandle
                    }.ToJsonString()
                });
            }
        }
    }
}
