using System.Text.Json.Nodes;
using Core.Import;
using Core.Rules.Engine;
using Core.Rules.Rules;
using Core.Store;
using Xunit;

namespace Core.Rules.Tests;

public class MiniSiteRulesTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Core.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException();
    }

    private static (string CorePath, string ReviewPath, string SnapshotId) ImportAndCheck()
    {
        var root = RepoRoot();
        var corePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".core");
        var reviewPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".corereview");
        var import = new Importer().Import(
            Path.Combine(root, "fixtures/corex/mini-site/package"),
            corePath,
            new ImportOptions { AiEnabled = false, MappingProfilePath = Path.Combine(root, "profiles/default.mapping-profile.json") });
        Assert.True(import.Succeeded);

        using var core = CoreStore.OpenReadOnly(corePath);
        var projectId = ReadScalar(core, "SELECT id FROM project LIMIT 1");
        using var review = ReviewStore.Create(reviewPath, projectId);
        var profile = RuleProfile.Load(Path.Combine(root, "profiles/default.rule-profile.json"));
        var engine = new RuleEngine([
            new SourceReferenceRule(),
            new NetworkConnectivityRule(),
            new SheetCoverageRule(),
            new AnnotationVsModelRule()
        ]);
        var coreSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(corePath))).ToLowerInvariant();
        engine.Run(core, review, import.SnapshotId, coreSha, "x", corePath, profile);
        return (corePath, reviewPath, import.SnapshotId);
    }

    private static string ReadScalar(CoreStore store, string sql)
    {
        using var cmd = store.Connection.CreateCommand();
        cmd.CommandText = sql;
        return (string)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void EmptyProfile_SucceedsWithNoRuleRuns()
    {
        var root = RepoRoot();
        var corePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".core");
        var reviewPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".corereview");
        var import = new Importer().Import(
            Path.Combine(root, "fixtures/corex/mini-site/package"),
            corePath,
            new ImportOptions { AiEnabled = false, MappingProfilePath = Path.Combine(root, "profiles/default.mapping-profile.json") });
        var emptyProfilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(emptyProfilePath, """
            {"schema":"core.rule-profile/1","name":"empty","source":"test","rules":{}}
            """);
        using var core = CoreStore.OpenReadOnly(corePath);
        using var review = ReviewStore.Create(reviewPath, ReadScalar(core, "SELECT id FROM project LIMIT 1"));
        var profile = RuleProfile.Load(emptyProfilePath);
        var engine = new RuleEngine([
            new SourceReferenceRule(), new NetworkConnectivityRule(), new SheetCoverageRule(), new AnnotationVsModelRule()
        ]);
        engine.Run(core, review, import.SnapshotId, "a", "b", corePath, profile);
        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM rule_run;";
        Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void MiniSite_FindingsMatchExpected()
    {
        var (_, reviewPath, snapshotId) = ImportAndCheck();
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "fixtures/corex/mini-site/expected/findings.json")))!;
        using var review = ReviewStore.Open(reviewPath);

        using var cmd = review.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT rr.rule_id, f.code, f.severity, f.basis, f.subject_key, f.facts_json
            FROM finding f JOIN rule_run rr ON rr.id = f.rule_run_id
            WHERE rr.snapshot_id = $s ORDER BY f.id;
            """;
        cmd.Parameters.AddWithValue("$s", snapshotId);
        var actual = new List<JsonObject>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var parts = r.GetString(4).Split(':');
                actual.Add(new JsonObject
                {
                    ["rule"] = r.GetString(0),
                    ["code"] = r.GetString(1),
                    ["severity"] = r.GetString(2),
                    ["basis"] = r.GetString(3),
                    ["subject"] = new JsonObject { ["drawing"] = parts[0], ["handle"] = parts[1] },
                    ["facts"] = JsonNode.Parse(r.GetString(5))
                });
            }
        }

        foreach (var ef in expected["findings"]!.AsArray())
        {
            var rule = ef!["rule"]!.GetValue<string>();
            var code = ef["code"]!.GetValue<string>();
            var drawing = ef["subject"]!["drawing"]!.GetValue<string>();
            var handle = ef["subject"]!["handle"]!.GetValue<string>();
            Assert.True(
                actual.Any(a =>
                    a["rule"]!.GetValue<string>() == rule &&
                    a["code"]!.GetValue<string>() == code &&
                    a["subject"]!["drawing"]!.GetValue<string>() == drawing &&
                    a["subject"]!["handle"]!.GetValue<string>() == handle &&
                    a["severity"]!.GetValue<string>() == ef["severity"]!.GetValue<string>() &&
                    a["basis"]!.GetValue<string>() == ef["basis"]!.GetValue<string>()),
                $"Missing finding {rule} {code} {drawing}:{handle}. Actual: {string.Join("; ", actual.Select(a => a["rule"]+":"+a["code"]+":"+a["subject"]))}");
        }

        foreach (var banned in expected["mustNotReport"]!.AsArray())
        {
            var rule = banned!["rule"]!.GetValue<string>();
            var drawing = banned["subject"]!["drawing"]!.GetValue<string>();
            var handle = banned["subject"]!["handle"]!.GetValue<string>();
            Assert.DoesNotContain(actual, a =>
                a["rule"]!.GetValue<string>() == rule &&
                a["subject"]!["drawing"]!.GetValue<string>() == drawing &&
                a["subject"]!["handle"]!.GetValue<string>() == handle);
        }

        // observations
        using var ocmd = review.Connection.CreateCommand();
        ocmd.CommandText = """
            SELECT rr.rule_id, o.code, o.subject_key FROM observation o
            JOIN rule_run rr ON rr.id = o.rule_run_id WHERE rr.snapshot_id = $s;
            """;
        ocmd.Parameters.AddWithValue("$s", snapshotId);
        var obs = new List<(string Rule, string Code, string Subject)>();
        using (var r = ocmd.ExecuteReader())
            while (r.Read()) obs.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        foreach (var eo in expected["observations"]!.AsArray())
        {
            var rule = eo!["rule"]!.GetValue<string>();
            var code = eo["code"]!.GetValue<string>();
            var subject = eo["subject"]!["drawing"]!.GetValue<string>() + ":" + eo["subject"]!["handle"]!.GetValue<string>();
            Assert.Contains(obs, o => o.Rule == rule && o.Code == code && o.Subject == subject);
        }
    }

    [Fact]
    public void UnitCoverage_ConnectionMismatchAndCalloutAmbiguousCodesExist()
    {
        // Codes from the specs that fixtures may not exercise still exist in rule implementations.
        var netSrc = File.ReadAllText(Path.Combine(RepoRoot(), "src/Core.Rules/Rules/NetworkConnectivityRule.cs"));
        var annSrc = File.ReadAllText(Path.Combine(RepoRoot(), "src/Core.Rules/Rules/AnnotationVsModelRule.cs"));
        Assert.Contains("connection-mismatch", netSrc);
        Assert.Contains("callout-ambiguous", annSrc);
        Assert.Contains("callout-unlinked", annSrc);
        Assert.Equal("core.network-connectivity", new NetworkConnectivityRule().Id);
        Assert.Equal("core.annotation-vs-model", new AnnotationVsModelRule().Id);
    }
}
