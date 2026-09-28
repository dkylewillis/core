using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace Core.Rules.Engine;

public sealed class RuleProfile
{
    public required string Name { get; init; }
    public required string Source { get; init; }
    public required string ContentJson { get; init; }
    public required string Sha256 { get; init; }
    public required JsonObject Rules { get; init; }

    public static RuleProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json)!.AsObject();
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        return new RuleProfile
        {
            Name = node["name"]!.GetValue<string>(),
            Source = node["source"]!.GetValue<string>(),
            ContentJson = json,
            Sha256 = sha,
            Rules = node["rules"]!.AsObject()
        };
    }

    public bool IsEnabled(string ruleId) =>
        Rules.TryGetPropertyValue(ruleId, out var r) && r!["enabled"]!.GetValue<bool>();

    public JsonObject Params(string ruleId) =>
        Rules[ruleId]!["params"]!.AsObject();
}
