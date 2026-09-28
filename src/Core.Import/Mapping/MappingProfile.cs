using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Core.Import.Mapping;

public sealed class MappingProfile
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public List<NetworkSystemRule> NetworkSystems { get; set; } = [];
}

public sealed class NetworkSystemRule
{
    public string Id { get; set; } = "";
    public string Match { get; set; } = "";
    public string System { get; set; } = "";

    [JsonIgnore]
    public Regex? Compiled { get; set; }
}

public static class MappingProfileLoader
{
    public static MappingProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = System.Text.Json.JsonSerializer.Deserialize<MappingProfile>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Invalid mapping profile.");
        foreach (var rule in profile.NetworkSystems)
            rule.Compiled = new Regex(rule.Match, RegexOptions.CultureInvariant);
        return profile;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
