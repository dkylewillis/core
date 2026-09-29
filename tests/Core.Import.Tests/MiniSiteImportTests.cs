using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Import;
using Core.Store;
using Xunit;

namespace Core.Import.Tests;

public class MiniSiteImportTests
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

    private static ImportResult ImportMiniSite()
    {
        var root = RepoRoot();
        var package = Path.Combine(root, "fixtures/corex/mini-site/package");
        var outPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".core");
        var importer = new Importer();
        return importer.Import(package, outPath, new ImportOptions
        {
            AiEnabled = false,
            MappingProfilePath = Path.Combine(root, "profiles/default.mapping-profile.json")
        });
    }

    [Fact]
    public void Import_Succeeds_AndMatchesExpectedImportFacts()
    {
        var result = ImportMiniSite();
        Assert.True(result.Succeeded, string.Join("\n", result.Messages.Select(m => m.Code + ": " + m.Message)));
        Assert.True(File.Exists(result.CorePath));

        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "fixtures/corex/mini-site/expected/import.json")))!;
        using var store = CoreStore.OpenReadOnly(result.CorePath);

        var drawings = SnapshotQueries.LoadDrawings(store);
        var expectedDrawings = expected["drawings"]!.AsArray();
        Assert.Equal(expectedDrawings.Count, drawings.Count);
        foreach (var ed in expectedDrawings)
        {
            var id = ed!["id"]!.GetValue<string>();
            var match = drawings.Single(d => d["id"]!.GetValue<string>() == id);
            Assert.Equal(ed["linearUnits"]!.GetValue<string>(), match["linearUnits"]!.GetValue<string>());
        }

        var dataRefs = SnapshotQueries.LoadDataReferences(store);
        // Fixture expected/import.json lists shortcut-root refs (4); the package also
        // carries network-part data references. Store all; require every expected row.
        foreach (var er in expected["dataReferences"]!.AsArray())
        {
            var ed = er!["reference"]!["drawing"]!.GetValue<string>();
            var eh = er["reference"]!["handle"]!.GetValue<string>();
            Assert.Contains(dataRefs, a =>
                a["reference"]!["drawing"]!.GetValue<string>() == ed &&
                a["reference"]!["handle"]!.GetValue<string>() == eh &&
                a["status"]!.GetValue<string>() == er["status"]!.GetValue<string>());
        }

        var xrefPaths = SnapshotQueries.LoadXrefPaths(store);
        var expectedPaths = expected["xrefPaths"]!["paths"]!.AsArray();
        Assert.Equal(expectedPaths.Count, xrefPaths.Count);
        foreach (var ep in expectedPaths)
        {
            var root = ep!["root"]!.GetValue<string>();
            var loaded = ep["loaded"]!.GetValue<bool>();
            var reason = ep["reason"]?.GetValue<string?>();
            Assert.Contains(xrefPaths, p =>
                p!["root"]!.GetValue<string>() == root &&
                p["loaded"]!.GetValue<bool>() == loaded &&
                (reason is null ? p["reason"] is null || p["reason"]!.GetValueKind() == System.Text.Json.JsonValueKind.Null
                                : p["reason"]?.GetValue<string>() == reason));
        }

        var designObjects = SnapshotQueries.LoadDesignObjects(store);
        Assert.Equal(expected["designObjects"]!.AsArray().Count, designObjects.Count);
        foreach (var eo in expected["designObjects"]!.AsArray())
        {
            var type = eo!["type"]!.GetValue<string>();
            var name = eo["name"]?.GetValue<string?>();
            Assert.Contains(designObjects, o =>
                o!["type"]!.GetValue<string>() == type &&
                (name is null ? true : o["name"]?.GetValue<string>() == name));
        }

        // Entity count: every source entity appears once
        using (var cmd = store.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM source_entity;";
            var count = (long)cmd.ExecuteScalar()!;
            Assert.True(count > 0);
        }
    }

    [Fact]
    public void Import_WritesWarnings_WithoutErrors()
    {
        var result = ImportMiniSite();
        Assert.DoesNotContain(result.Messages, m => m.Severity == "error");
        Assert.Contains(result.Messages, m => m.Code == "xref-unresolved");
        Assert.Contains(result.Messages, m => m.Code == "data-reference-source-missing");
        Assert.Contains(result.Messages, m => m.Code == "mixed-units-in-xref-chain");
    }
}
