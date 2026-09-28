namespace Core.Import;

internal sealed class DesignObjectProp
{
    public long Id;
    public string Type = "";
    public string? Name;
    public string? System;
    public string StableKey = "";
    public Dictionary<string, object?> Properties = new();
    public List<(string Drawing, string Handle, string Method, string? Via)> Mappings = [];
}
