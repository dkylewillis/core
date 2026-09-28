using Core.Import;
using Core.Report;
using Core.Rules.Engine;
using Core.Rules.Rules;
using Core.Store;
using Xunit;

namespace Core.Report.Tests;

public class ReportTests
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

    [Fact]
    public void HtmlReport_IsSelfContained_AndListsFindings()
    {
        var root = RepoRoot();
        var corePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".core");
        var reviewPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".corereview");
        var import = new Importer().Import(Path.Combine(root, "fixtures/corex/mini-site/package"), corePath,
            new ImportOptions { AiEnabled = false, MappingProfilePath = Path.Combine(root, "profiles/default.mapping-profile.json") });
        using var core = CoreStore.OpenReadOnly(corePath);
        using var review = ReviewStore.Create(reviewPath, (string)Scalar(core, "SELECT id FROM project"));
        var engine = new RuleEngine([new SourceReferenceRule(), new NetworkConnectivityRule(), new SheetCoverageRule(), new AnnotationVsModelRule()]);
        engine.Run(core, review, import.SnapshotId, "a", "b", corePath, RuleProfile.Load(Path.Combine(root, "profiles/default.rule-profile.json")));
        var html = HtmlReport.Generate(review, import.SnapshotId);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.DoesNotContain("http://", html.Split("charset")[1]); // no external requests after head meta
        Assert.Contains("core.source-reference", html);
        Assert.Contains("severity=", html);
        Directory.CreateDirectory("/opt/cursor/artifacts");
        File.WriteAllText("/opt/cursor/artifacts/mini-site-report.html", html);
    }

    static object Scalar(CoreStore s, string sql) { using var c=s.Connection.CreateCommand(); c.CommandText=sql; return c.ExecuteScalar()!; }
}
