using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Rules.Engine;
using Microsoft.Data.Sqlite;

namespace Core.Rules.Rules;

public sealed class SourceReferenceRule : IRule
{
    public string Id => "core.source-reference";
    public int Version => 1;
    public string Kind => "deterministic";
    public string SpecRef => "docs/rules/RULE-001-source-reference.md";

    public void Execute(RuleContext context)
    {
        var reportFallback = context.Params["reportFallbackResolution"]!.GetValue<bool>();
        var unloadedMode = context.Params["unloadedXref"]!.GetValue<string>();

        using (var cmd = context.Core.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT d.corex_id, xi.handle, xi.block_name, xi.saved_path, xi.resolved_path, xi.resolved_by, xi.status
                FROM xref_instance xi
                JOIN drawing d ON d.id = xi.host_drawing_id
                WHERE xi.status <> 'unreferenced'
                ORDER BY d.corex_id, xi.handle;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var drawing = r.GetString(0);
                var handle = r.GetString(1);
                var block = r.GetString(2);
                var saved = r.GetString(3);
                var resolved = r.IsDBNull(4) ? null : r.GetString(4);
                var by = r.GetString(5);
                var status = r.GetString(6);
                var subjectKey = $"{drawing}:{handle}";
                var facts = new JsonObject
                {
                    ["host"] = drawing,
                    ["blockName"] = block,
                    ["savedPath"] = saved,
                    ["resolvedPath"] = resolved,
                    ["status"] = status
                };

                if (status == "not-found")
                {
                    context.EmitFinding(new FindingDraft
                    {
                        FindingKey = $"{Id}|xref-not-found|{subjectKey}",
                        Code = "xref-not-found",
                        Severity = "high",
                        Basis = "native",
                        SubjectKey = subjectKey,
                        Title = $"Xref not found: {block}",
                        FactsJson = facts.ToJsonString()
                    });
                }
                else if (status == "unresolved")
                {
                    context.EmitFinding(new FindingDraft
                    {
                        FindingKey = $"{Id}|xref-unresolved|{subjectKey}",
                        Code = "xref-unresolved",
                        Severity = "high",
                        Basis = "native",
                        SubjectKey = subjectKey,
                        Title = $"Xref unresolved: {block}",
                        FactsJson = facts.ToJsonString()
                    });
                }
                else if (status == "unloaded")
                {
                    if (unloadedMode == "finding")
                    {
                        context.EmitFinding(new FindingDraft
                        {
                            FindingKey = $"{Id}|xref-unloaded|{subjectKey}",
                            Code = "xref-unloaded",
                            Severity = "medium",
                            Basis = "native",
                            SubjectKey = subjectKey,
                            Title = $"Xref unloaded: {block}",
                            FactsJson = facts.ToJsonString()
                        });
                    }
                    else if (unloadedMode == "observation")
                    {
                        context.EmitObservation(new ObservationDraft
                        {
                            Code = "xref-unloaded",
                            Basis = "native",
                            SubjectKey = subjectKey,
                            FactsJson = facts.ToJsonString()
                        });
                    }
                }
                else if (status == "resolved" && reportFallback && by is not ("saved-path" or "relative-to-host"))
                {
                    var obsFacts = new JsonObject
                    {
                        ["savedPath"] = saved,
                        ["resolvedPath"] = resolved,
                        ["resolvedBy"] = by
                    };
                    context.EmitObservation(new ObservationDraft
                    {
                        Code = "xref-resolved-by-fallback",
                        Basis = "native",
                        SubjectKey = subjectKey,
                        FactsJson = obsFacts.ToJsonString()
                    });
                }
            }
        }

        using (var cmd = context.Core.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT d.corex_id, se.handle, dr.shortcut_name, dr.status, s.object_type, s.source_path
                FROM data_reference dr
                JOIN source_entity se ON se.id = dr.reference_entity_id
                JOIN drawing d ON d.id = se.drawing_id
                LEFT JOIN shortcut s ON s.name = dr.shortcut_name
                ORDER BY d.corex_id, se.handle;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var drawing = r.GetString(0);
                var handle = r.GetString(1);
                var shortcut = r.GetString(2);
                var status = r.GetString(3);
                var objectType = r.IsDBNull(4) ? "unknown" : r.GetString(4);
                var sourcePath = r.IsDBNull(5) ? null : r.GetString(5);
                var subjectKey = $"{drawing}:{handle}";

                // Only emit findings for shortcut-root objects to match fixture expected subjects
                // (network parts map into DesignObjects and are not double-reported).
                if (status is "source-not-found" or "object-not-found" or "out-of-date")
                {
                    // Skip network parts: only report when entity is the primary shortcut object.
                    using var kindCmd = context.Core.Connection.CreateCommand();
                    kindCmd.CommandText = """
                        SELECT se.kind FROM source_entity se
                        JOIN drawing d ON d.id = se.drawing_id
                        WHERE d.corex_id = $d AND se.handle = $h;
                        """;
                    kindCmd.Parameters.AddWithValue("$d", drawing);
                    kindCmd.Parameters.AddWithValue("$h", handle);
                    var kind = (string)kindCmd.ExecuteScalar()!;
                    if (kind is "pipe" or "structure" or "pressure-pipe" or "pressure-fitting" or "pressure-appurtenance")
                        continue;

                    var code = status switch
                    {
                        "source-not-found" => "data-reference-source-not-found",
                        "object-not-found" => "data-reference-object-not-found",
                        _ => "data-reference-out-of-date"
                    };
                    var severity = status == "out-of-date" ? "medium" : "high";
                    var facts = new JsonObject
                    {
                        ["host"] = drawing,
                        ["shortcut"] = shortcut,
                        ["objectType"] = objectType,
                        ["sourcePath"] = sourcePath,
                        ["status"] = status
                    };
                    context.EmitFinding(new FindingDraft
                    {
                        FindingKey = $"{Id}|{code}|{subjectKey}",
                        Code = code,
                        Severity = severity,
                        Basis = "native",
                        SubjectKey = subjectKey,
                        Title = $"Data reference {status}: {shortcut}",
                        FactsJson = facts.ToJsonString()
                    });
                }
            }
        }
    }
}
