using System.Net;
using System.Text;
using Core.Store;

namespace Core.Report;

public static class HtmlReport
{
    public static string Generate(ReviewStore review, string snapshotId)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html><html><head><meta charset="utf-8"/><title>CORE Findings Report</title>
            <style>
            body{font-family:Georgia,serif;margin:2rem;background:#f7f4ef;color:#1c1a16}
            h1{font-size:1.8rem} h2{margin-top:2rem;border-bottom:1px solid #ccc;padding-bottom:.3rem}
            .finding{margin:1rem 0;padding:1rem 0;border-bottom:1px dotted #bbb}
            .sev-high{color:#8b1e1e}.sev-medium{color:#8a5a00}.sev-low{color:#335}
            .meta{color:#555;font-size:.9rem} pre{white-space:pre-wrap;background:#efebe3;padding:.75rem}
            </style></head><body>
            """);
        sb.Append("<h1>CORE Findings Report</h1>");
        sb.Append("<p class=\"meta\">Snapshot ").Append(WebUtility.HtmlEncode(snapshotId)).Append("</p>");

        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT rr.rule_id, rr.rule_version, rr.params_json, f.code, f.severity, f.basis, f.subject_key, f.sheet_number, f.title, f.status, f.facts_json, f.id
            FROM finding f
            JOIN rule_run rr ON rr.id = f.rule_run_id
            WHERE rr.snapshot_id = $s
            ORDER BY rr.rule_id, f.sheet_number, f.id;
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        string? currentRule = null;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var rule = r.GetString(0);
            if (rule != currentRule)
            {
                currentRule = rule;
                sb.Append("<h2>").Append(WebUtility.HtmlEncode(rule)).Append("</h2>");
            }
            var sev = r.GetString(4);
            sb.Append("<div class=\"finding\">");
            sb.Append("<div class=\"sev-").Append(sev).Append("\"><strong>")
              .Append(WebUtility.HtmlEncode(r.GetString(8))).Append("</strong></div>");
            sb.Append("<div class=\"meta\">severity=").Append(sev)
              .Append(" basis=").Append(WebUtility.HtmlEncode(r.GetString(5)))
              .Append(" status=").Append(WebUtility.HtmlEncode(r.GetString(9)))
              .Append(" ruleVersion=").Append(r.GetInt64(1))
              .Append(" subject=").Append(WebUtility.HtmlEncode(r.GetString(6)));
            if (!r.IsDBNull(7)) sb.Append(" sheet=").Append(WebUtility.HtmlEncode(r.GetString(7)));
            sb.Append("</div>");
            sb.Append("<div class=\"meta\">profile params: <code>")
              .Append(WebUtility.HtmlEncode(r.GetString(2))).Append("</code></div>");
            sb.Append("<pre>").Append(WebUtility.HtmlEncode(r.GetString(10))).Append("</pre>");

            // evidence
            using var ecmd = review.Connection.CreateCommand();
            ecmd.CommandText = "SELECT role, target_kind, target_key, values_json FROM evidence WHERE finding_id = $id;";
            ecmd.Parameters.AddWithValue("$id", r.GetInt64(11));
            using var er = ecmd.ExecuteReader();
            while (er.Read())
            {
                sb.Append("<div class=\"meta\">evidence ").Append(WebUtility.HtmlEncode(er.GetString(0)))
                  .Append(" ").Append(WebUtility.HtmlEncode(er.GetString(1)))
                  .Append(" ").Append(WebUtility.HtmlEncode(er.GetString(2)))
                  .Append(" ").Append(WebUtility.HtmlEncode(er.GetString(3))).Append("</div>");
            }
            sb.Append("</div>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
