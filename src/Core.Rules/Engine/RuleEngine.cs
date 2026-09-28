using System.Text.Json;
using Core.Store;
using Microsoft.Data.Sqlite;

namespace Core.Rules.Engine;

public sealed class RuleEngine
{
    private readonly Dictionary<string, IRule> _rules;

    public RuleEngine(IEnumerable<IRule> rules)
    {
        _rules = rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
    }

    public void EnsureRegistered(ReviewStore review)
    {
        foreach (var rule in _rules.Values)
        {
            using var cmd = review.Connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO rule(id, version, kind, spec_ref) VALUES ($id,$v,$k,$s);";
            cmd.Parameters.AddWithValue("$id", rule.Id);
            cmd.Parameters.AddWithValue("$v", rule.Version);
            cmd.Parameters.AddWithValue("$k", rule.Kind);
            cmd.Parameters.AddWithValue("$s", rule.SpecRef);
            cmd.ExecuteNonQuery();
        }
    }

    public void Run(CoreStore core, ReviewStore review, string snapshotId, string coreSha256, string corexSha256, string? corePath, RuleProfile profile, IEnumerable<string>? onlyRules = null)
    {
        EnsureRegistered(review);
        EnsureSnapshotRef(review, snapshotId, coreSha256, corexSha256, corePath);
        EnsureProfile(review, profile);

        var enabled = _rules.Values
            .Where(r => profile.IsEnabled(r.Id))
            .Where(r => onlyRules == null || onlyRules.Contains(r.Id))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var rule in enabled)
        {
            var runId = StartRun(review, rule, profile, snapshotId);
            try
            {
                var ctx = new RuleContext
                {
                    Core = core,
                    Review = review,
                    SnapshotId = snapshotId,
                    RuleRunId = runId,
                    Params = profile.Params(rule.Id),
                    EmitObservation = o => InsertObservation(review, runId, o),
                    EmitFinding = f => InsertFinding(review, runId, f)
                };
                rule.Execute(ctx);
                FinishRun(review, runId, "succeeded", null);
            }
            catch (Exception ex)
            {
                FinishRun(review, runId, "failed", ex.Message);
                throw;
            }
        }
    }

    private static void EnsureSnapshotRef(ReviewStore review, string snapshotId, string coreSha, string corexSha, string? path)
    {
        using var check = review.Connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM snapshot_ref WHERE snapshot_id = $id;";
        check.Parameters.AddWithValue("$id", snapshotId);
        if (check.ExecuteScalar() != null) return;

        long seq;
        using (var s = review.Connection.CreateCommand())
        {
            s.CommandText = "SELECT COALESCE(MAX(sequence), 0) + 1 FROM snapshot_ref;";
            seq = (long)s.ExecuteScalar()!;
        }
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO snapshot_ref(snapshot_id, core_sha256, corex_sha256, core_path, added_at, sequence) VALUES ($id,$c,$x,$p,$a,$seq);";
        cmd.Parameters.AddWithValue("$id", snapshotId);
        cmd.Parameters.AddWithValue("$c", coreSha);
        cmd.Parameters.AddWithValue("$x", corexSha);
        cmd.Parameters.AddWithValue("$p", (object?)path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$seq", seq);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureProfile(ReviewStore review, RuleProfile profile)
    {
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO rule_profile(sha256, name, source, content_json) VALUES ($s,$n,$src,$c);";
        cmd.Parameters.AddWithValue("$s", profile.Sha256);
        cmd.Parameters.AddWithValue("$n", profile.Name);
        cmd.Parameters.AddWithValue("$src", profile.Source);
        cmd.Parameters.AddWithValue("$c", profile.ContentJson);
        cmd.ExecuteNonQuery();
    }

    private static long StartRun(ReviewStore review, IRule rule, RuleProfile profile, string snapshotId)
    {
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rule_run(rule_id, rule_version, profile_sha256, snapshot_id, params_json, engine_version, started_at, status)
            VALUES ($id,$v,$p,$snap,$params,'0.1.0',$t,'running'); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$id", rule.Id);
        cmd.Parameters.AddWithValue("$v", rule.Version);
        cmd.Parameters.AddWithValue("$p", profile.Sha256);
        cmd.Parameters.AddWithValue("$snap", snapshotId);
        cmd.Parameters.AddWithValue("$params", profile.Params(rule.Id).ToJsonString());
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    private static void FinishRun(ReviewStore review, long runId, string status, string? error)
    {
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = "UPDATE rule_run SET finished_at = $t, status = $s, error = $e WHERE id = $id;";
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", runId);
        cmd.ExecuteNonQuery();
    }

    private static void InsertObservation(ReviewStore review, long runId, ObservationDraft o)
    {
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO observation(rule_run_id, code, basis, subject_key, facts_json) VALUES ($r,$c,$b,$s,$f);";
        cmd.Parameters.AddWithValue("$r", runId);
        cmd.Parameters.AddWithValue("$c", o.Code);
        cmd.Parameters.AddWithValue("$b", o.Basis);
        cmd.Parameters.AddWithValue("$s", o.SubjectKey);
        cmd.Parameters.AddWithValue("$f", o.FactsJson);
        cmd.ExecuteNonQuery();
    }

    private static void InsertFinding(ReviewStore review, long runId, FindingDraft f)
    {
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO finding(rule_run_id, finding_key, code, severity, basis, subject_key, sheet_number, title, facts_json, status)
            VALUES ($r,$k,$c,$sev,$b,$s,$sheet,$t,$f,'open'); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$r", runId);
        cmd.Parameters.AddWithValue("$k", f.FindingKey);
        cmd.Parameters.AddWithValue("$c", f.Code);
        cmd.Parameters.AddWithValue("$sev", f.Severity);
        cmd.Parameters.AddWithValue("$b", f.Basis);
        cmd.Parameters.AddWithValue("$s", f.SubjectKey);
        cmd.Parameters.AddWithValue("$sheet", (object?)f.SheetNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", f.Title);
        cmd.Parameters.AddWithValue("$f", f.FactsJson);
        var findingId = (long)cmd.ExecuteScalar()!;
        foreach (var e in f.Evidence)
        {
            using var ecmd = review.Connection.CreateCommand();
            ecmd.CommandText = "INSERT INTO evidence(finding_id, role, target_kind, target_key, values_json) VALUES ($f,$role,$tk,$tkey,$v);";
            ecmd.Parameters.AddWithValue("$f", findingId);
            ecmd.Parameters.AddWithValue("$role", e.Role);
            ecmd.Parameters.AddWithValue("$tk", e.TargetKind);
            ecmd.Parameters.AddWithValue("$tkey", e.TargetKey);
            ecmd.Parameters.AddWithValue("$v", e.ValuesJson);
            ecmd.ExecuteNonQuery();
        }
    }
}
