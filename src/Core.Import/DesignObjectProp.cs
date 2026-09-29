namespace Core.Import;

internal sealed class DesignObjectProp
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public string? System { get; set; }
    public string StableKey { get; set; } = "";
    public Dictionary<string, object?> Properties { get; set; } = new();
    public List<(string Drawing, string Handle, string Method, string? Via)> Mappings { get; set; } = [];
}
