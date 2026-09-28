namespace Core.Import;

public sealed class ImportOptions
{
    public bool AiEnabled { get; init; }
    public string? AiModel { get; init; }
    public string MappingProfilePath { get; init; } = "";
    public string ImporterVersion { get; init; } = "0.1.0";
}
