using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Import;
using Core.Report;
using Core.Rules;
using Core.Rules.Engine;
using Core.Rules.Rules;
using Core.Store;
using Microsoft.Data.Sqlite;

var jsonOpt = new Option<bool>("--json", () => false, "Emit machine-readable JSON");

var importCmd = new Command("import", "Import a COREX package to a .core snapshot");
var importPkg = new Argument<string>("package", "Path to .corex file or unpacked directory");
var importOut = new Option<string>("--out", "Output .core path") { IsRequired = true };
var noAi = new Option<bool>("--no-ai", () => true, "Disable AI during import");
var mappingOpt = new Option<string?>("--mapping-profile", () => null);
importCmd.AddArgument(importPkg);
importCmd.AddOption(importOut);
importCmd.AddOption(noAi);
importCmd.AddOption(mappingOpt);
importCmd.AddOption(jsonOpt);
importCmd.SetHandler((string package, string outPath, bool noAiFlag, string? mapping, bool json) =>
{
    var root = FindRepoRoot() ?? Directory.GetCurrentDirectory();
    mapping ??= Path.Combine(root, "profiles", "default.mapping-profile.json");
    var result = new Importer().Import(package, outPath, new ImportOptions
    {
        AiEnabled = !noAiFlag,
        MappingProfilePath = mapping
    });
    if (json)
    {
        var node = new JsonObject
        {
            ["snapshotId"] = result.SnapshotId,
            ["warnings"] = new JsonArray(result.Messages.Where(m => m.Severity == "warning").Select(m => JsonValue.Create(m.Code)).ToArray()),
            ["errors"] = new JsonArray(result.Messages.Where(m => m.Severity == "error").Select(m => JsonValue.Create(m.Code)).ToArray())
        };
        Console.WriteLine(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine(result.Succeeded
            ? $"Imported snapshot {result.SnapshotId} -> {outPath}"
            : "Import failed with validation errors.");
        foreach (var m in result.Messages)
            Console.WriteLine($"{m.Severity}: {m.Code} — {m.Message}");
    }
    Environment.ExitCode = result.Succeeded ? 0 : 1;
}, importPkg, importOut, noAi, mappingOpt, jsonOpt);

var validateCmd = new Command("validate", "Validate an existing .core snapshot");
var validateCore = new Argument<string>("core");
validateCmd.AddArgument(validateCore);
validateCmd.AddOption(jsonOpt);
validateCmd.SetHandler((string core, bool json) =>
{
    using var store = CoreStore.OpenReadOnly(core);
    using var cmd = store.Connection.CreateCommand();
    cmd.CommandText = "SELECT severity, code, message FROM validation_message ORDER BY id;";
    var errors = 0;
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        if (r.GetString(0) == "error") errors++;
        if (!json) Console.WriteLine($"{r.GetString(0)}: {r.GetString(1)} — {r.GetString(2)}");
    }
    Environment.ExitCode = errors == 0 ? 0 : 1;
}, validateCore, jsonOpt);

var checkCmd = new Command("check", "Run QC rules");
var checkCore = new Argument<string>("core");
var reviewOpt = new Option<string>("--review") { IsRequired = true };
var profileOpt = new Option<string>("--profile") { IsRequired = true };
var ruleOpt = new Option<string[]>("--rule", () => Array.Empty<string>());
checkCmd.AddArgument(checkCore);
checkCmd.AddOption(reviewOpt);
checkCmd.AddOption(profileOpt);
checkCmd.AddOption(ruleOpt);
checkCmd.AddOption(jsonOpt);
checkCmd.SetHandler((string core, string review, string profilePath, string[] rules, bool json) =>
{
    using var coreStore = CoreStore.OpenReadOnly(core);
    var snapshotId = MigrationRunner.GetMeta(coreStore.Connection, "snapshot_id")!;
    var projectId = ReadProjectId(coreStore);
    var reviewStore = File.Exists(review) ? ReviewStore.Open(review) : ReviewStore.Create(review, projectId);
    using (reviewStore)
    {
        var profile = RuleProfile.Load(profilePath);
        var engine = CreateEngine();
        var coreSha = ShaFile(core);
        var corexSha = ReadCorexSha(coreStore);
        engine.Run(coreStore, reviewStore, snapshotId, coreSha, corexSha, core, profile, rules.Length == 0 ? null : rules);
        if (json) Console.WriteLine(JsonSerializer.Serialize(new { snapshotId, status = "ok" }));
        else Console.WriteLine($"Checked snapshot {snapshotId}");
    }
}, checkCore, reviewOpt, profileOpt, ruleOpt, jsonOpt);

var findingsCmd = new Command("findings", "List findings");
var findingsReview = new Argument<string>("review");
var snapOpt = new Option<string?>("--snapshot", () => null);
var statusOpt = new Option<string?>("--status", () => null);
findingsCmd.AddArgument(findingsReview);
findingsCmd.AddOption(snapOpt);
findingsCmd.AddOption(statusOpt);
findingsCmd.AddOption(jsonOpt);
findingsCmd.SetHandler((string review, string? snapshot, string? status, bool json) =>
{
    using var store = ReviewStore.Open(review);
    using var cmd = store.Connection.CreateCommand();
    cmd.CommandText = """
        SELECT f.id, rr.rule_id, f.code, f.severity, f.basis, f.subject_key, f.status, f.facts_json
        FROM finding f JOIN rule_run rr ON rr.id = f.rule_run_id
        WHERE ($snap IS NULL OR rr.snapshot_id = $snap)
          AND ($status IS NULL OR f.status = $status)
        ORDER BY f.id;
        """;
    cmd.Parameters.AddWithValue("$snap", (object?)snapshot ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$status", (object?)status ?? DBNull.Value);
    var arr = new JsonArray();
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        var obj = new JsonObject
        {
            ["id"] = r.GetInt64(0),
            ["rule"] = r.GetString(1),
            ["code"] = r.GetString(2),
            ["severity"] = r.GetString(3),
            ["basis"] = r.GetString(4),
            ["subjectKey"] = r.GetString(5),
            ["status"] = r.GetString(6),
            ["facts"] = JsonNode.Parse(r.GetString(7))
        };
        arr.Add(obj);
        if (!json) Console.WriteLine($"{r.GetInt64(0)} {r.GetString(1)} {r.GetString(2)} {r.GetString(5)} [{r.GetString(6)}]");
    }
    if (json) Console.WriteLine(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}, findingsReview, snapOpt, statusOpt, jsonOpt);

var disposeCmd = new Command("dispose", "Record a disposition on a finding");
var disposeReview = new Argument<string>("review");
var findingIdArg = new Argument<long>("finding-id");
var decisionOpt = new Option<string>("--decision") { IsRequired = true };
var reasonOpt = new Option<string>("--reason") { IsRequired = true };
var reviewerOpt = new Option<string>("--reviewer", () => Environment.UserName);
disposeCmd.AddArgument(disposeReview);
disposeCmd.AddArgument(findingIdArg);
disposeCmd.AddOption(decisionOpt);
disposeCmd.AddOption(reasonOpt);
disposeCmd.AddOption(reviewerOpt);
disposeCmd.SetHandler((string review, long findingId, string decision, string reason, string reviewer) =>
{
    using var store = ReviewStore.Open(review);
    using var cmd = store.Connection.CreateCommand();
    cmd.CommandText = "INSERT INTO disposition(finding_id, decision, reason, reviewer, decided_at) VALUES ($f,$d,$r,$rev,$t);";
    cmd.Parameters.AddWithValue("$f", findingId);
    cmd.Parameters.AddWithValue("$d", decision);
    cmd.Parameters.AddWithValue("$r", reason);
    cmd.Parameters.AddWithValue("$rev", reviewer);
    cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
    cmd.ExecuteNonQuery();
    using var upd = store.Connection.CreateCommand();
    upd.CommandText = "UPDATE finding SET status = $s WHERE id = $id;";
    upd.Parameters.AddWithValue("$s", decision);
    upd.Parameters.AddWithValue("$id", findingId);
    upd.ExecuteNonQuery();
}, disposeReview, findingIdArg, decisionOpt, reasonOpt, reviewerOpt);

var carryCmd = new Command("carry", "Carry findings from one snapshot to another");
var carryReview = new Argument<string>("review");
var fromOpt = new Option<string>("--from") { IsRequired = true };
var toOpt = new Option<string>("--to") { IsRequired = true };
carryCmd.AddArgument(carryReview);
carryCmd.AddOption(fromOpt);
carryCmd.AddOption(toOpt);
carryCmd.SetHandler((string review, string from, string to) =>
{
    using var store = ReviewStore.Open(review);
    CarryForward.Run(store, from, to);
}, carryReview, fromOpt, toOpt);

var reportCmd = new Command("report", "Generate HTML findings report");
var reportReview = new Argument<string>("review");
var reportSnap = new Option<string>("--snapshot") { IsRequired = true };
var reportOut = new Option<string>("--out") { IsRequired = true };
reportCmd.AddArgument(reportReview);
reportCmd.AddOption(reportSnap);
reportCmd.AddOption(reportOut);
reportCmd.SetHandler((string review, string snapshot, string outPath) =>
{
    using var store = ReviewStore.Open(review);
    var html = HtmlReport.Generate(store, snapshot);
    File.WriteAllText(outPath, html);
    Console.WriteLine($"Wrote {outPath}");
}, reportReview, reportSnap, reportOut);

var diffCmd = new Command("diff", "Diff two .core snapshots");
var oldCore = new Argument<string>("old");
var newCore = new Argument<string>("new");
diffCmd.AddArgument(oldCore);
diffCmd.AddArgument(newCore);
diffCmd.AddOption(jsonOpt);
diffCmd.SetHandler((string oldPath, string newPath, bool json) =>
{
    var diff = SnapshotDiff.Compare(oldPath, newPath);
    Console.WriteLine(diff.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}, oldCore, newCore, jsonOpt);

var root = new RootCommand("CORE — Civil Object Review Engine");
root.AddCommand(importCmd);
root.AddCommand(validateCmd);
root.AddCommand(checkCmd);
root.AddCommand(findingsCmd);
root.AddCommand(disposeCmd);
root.AddCommand(carryCmd);
root.AddCommand(reportCmd);
root.AddCommand(diffCmd);
return await root.InvokeAsync(args);

static RuleEngine CreateEngine() => new([
    new SourceReferenceRule(),
    new NetworkConnectivityRule(),
    new SheetCoverageRule(),
    new AnnotationVsModelRule()
]);

static string? FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Core.sln"))) return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

static string ReadProjectId(CoreStore store)
{
    using var cmd = store.Connection.CreateCommand();
    cmd.CommandText = "SELECT id FROM project LIMIT 1;";
    return (string)cmd.ExecuteScalar()!;
}

static string ReadCorexSha(CoreStore store)
{
    using var cmd = store.Connection.CreateCommand();
    cmd.CommandText = "SELECT corex_sha256 FROM snapshot LIMIT 1;";
    return (string)cmd.ExecuteScalar()!;
}

static string ShaFile(string path)
{
    using var s = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
}
