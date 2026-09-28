namespace Core.Import;

public sealed class ImportResult
{
    public required string SnapshotId { get; init; }
    public required string CorePath { get; init; }
    public required IReadOnlyList<ValidationMessage> Messages { get; init; }
    public bool Succeeded => Messages.All(m => m.Severity != "error");
}

public sealed class ValidationMessage
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? SubjectJson { get; init; }
}
