using Core.Store;
using Microsoft.Data.Sqlite;

namespace Core.Rules;

public static class CarryForward
{
    public static void Run(ReviewStore review, string fromSnapshotId, string toSnapshotId)
    {
        // Load findings from latest runs on each snapshot
        var fromFindings = LoadFindings(review, fromSnapshotId);
        var toFindings = LoadFindings(review, toSnapshotId);

        var matchedTo = new HashSet<long>();
        foreach (var from in fromFindings)
        {
            var exact = toFindings.FirstOrDefault(t => t.FindingKey == from.FindingKey);
            if (exact != null)
            {
                matchedTo.Add(exact.Id);
                Inherit(review, from, exact.Id, "exact", from.Status);
                continue;
            }

            // Fallback: same rule+code+name from facts
            var fallback = toFindings.FirstOrDefault(t =>
                t.RuleId == from.RuleId && t.Code == from.Code &&
                ExtractName(t.FactsJson) != null && ExtractName(t.FactsJson) == ExtractName(from.FactsJson));
            if (fallback != null)
            {
                matchedTo.Add(fallback.Id);
                // fallback does not inherit disposition; mark needs-confirmation
                using var upd = review.Connection.CreateCommand();
                upd.CommandText = "UPDATE finding SET status = 'needs-confirmation', carried_from_id = $from, carry_match = 'fallback' WHERE id = $id;";
                upd.Parameters.AddWithValue("$from", from.Id);
                upd.Parameters.AddWithValue("$id", fallback.Id);
                upd.ExecuteNonQuery();
                continue;
            }

            // No match in new snapshot → resolved-by-change (keep historical finding)
            using var resolved = review.Connection.CreateCommand();
            resolved.CommandText = "UPDATE finding SET status = 'resolved-by-change' WHERE id = $id AND status NOT IN ('resolved-by-change');";
            // Actually: mark a copy? Spec says earlier findings with no match are marked resolved-by-change
            resolved.CommandText = "UPDATE finding SET status = 'resolved-by-change' WHERE id = $id;";
            resolved.Parameters.AddWithValue("$id", from.Id);
            resolved.ExecuteNonQuery();
        }
    }

    private static void Inherit(ReviewStore review, FindingRow from, long toId, string match, string status)
    {
        using var upd = review.Connection.CreateCommand();
        upd.CommandText = "UPDATE finding SET status = $s, carried_from_id = $from, carry_match = $m WHERE id = $id;";
        upd.Parameters.AddWithValue("$s", status is "open" or "needs-confirmation" ? status : status);
        upd.Parameters.AddWithValue("$from", from.Id);
        upd.Parameters.AddWithValue("$m", match);
        upd.Parameters.AddWithValue("$id", toId);
        upd.ExecuteNonQuery();

        // Copy latest disposition if any
        using var disp = review.Connection.CreateCommand();
        disp.CommandText = "SELECT decision, reason, reviewer, decided_at FROM disposition WHERE finding_id = $id ORDER BY id DESC LIMIT 1;";
        disp.Parameters.AddWithValue("$id", from.Id);
        using var r = disp.ExecuteReader();
        if (r.Read())
        {
            using var ins = review.Connection.CreateCommand();
            ins.CommandText = "INSERT INTO disposition(finding_id, decision, reason, reviewer, decided_at, inherited_from_id) VALUES ($f,$d,$r,$rev,$t,(SELECT id FROM disposition WHERE finding_id = $from ORDER BY id DESC LIMIT 1));";
            ins.Parameters.AddWithValue("$f", toId);
            ins.Parameters.AddWithValue("$d", r.GetString(0));
            ins.Parameters.AddWithValue("$r", r.GetString(1));
            ins.Parameters.AddWithValue("$rev", r.GetString(2));
            ins.Parameters.AddWithValue("$t", r.GetString(3));
            ins.Parameters.AddWithValue("$from", from.Id);
            ins.ExecuteNonQuery();
            using var st = review.Connection.CreateCommand();
            st.CommandText = "UPDATE finding SET status = $s WHERE id = $id;";
            st.Parameters.AddWithValue("$s", r.GetString(0));
            st.Parameters.AddWithValue("$id", toId);
            st.ExecuteNonQuery();
        }
    }

    private static string? ExtractName(string factsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(factsJson);
            if (doc.RootElement.TryGetProperty("pipe", out var p)) return p.GetString();
            if (doc.RootElement.TryGetProperty("structure", out var s)) return s.GetString();
            if (doc.RootElement.TryGetProperty("designObject", out var d)) return d.GetString();
        }
        catch { }
        return null;
    }

    private sealed record FindingRow(long Id, string RuleId, string Code, string FindingKey, string Status, string FactsJson);

    private static List<FindingRow> LoadFindings(ReviewStore review, string snapshotId)
    {
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT f.id, rr.rule_id, f.code, f.finding_key, f.status, f.facts_json
            FROM finding f
            JOIN rule_run rr ON rr.id = f.rule_run_id
            WHERE rr.snapshot_id = $s AND rr.id IN (
              SELECT MAX(id) FROM rule_run WHERE snapshot_id = $s GROUP BY rule_id
            );
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        var list = new List<FindingRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FindingRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        return list;
    }
}
