using Core.Store;
using System.Text.Json.Nodes;

namespace Core.Rules.Engine;

public interface IRule
{
    string Id { get; }
    int Version { get; }
    string Kind { get; } // deterministic | ai-assisted
    string SpecRef { get; }
    void Execute(RuleContext context);
}

public sealed class RuleContext
{
    public required CoreStore Core { get; init; }
    public required ReviewStore Review { get; init; }
    public required string SnapshotId { get; init; }
    public required long RuleRunId { get; init; }
    public required JsonObject Params { get; init; }
    public required Action<ObservationDraft> EmitObservation { get; init; }
    public required Action<FindingDraft> EmitFinding { get; init; }
}

public sealed class ObservationDraft
{
    public required string Code { get; init; }
    public required string Basis { get; init; }
    public required string SubjectKey { get; init; }
    public required string FactsJson { get; init; }
}

public sealed class FindingDraft
{
    public required string FindingKey { get; init; }
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Basis { get; init; }
    public required string SubjectKey { get; init; }
    public string? SheetNumber { get; init; }
    public required string Title { get; init; }
    public required string FactsJson { get; init; }
    public List<EvidenceDraft> Evidence { get; init; } = [];
}

public sealed class EvidenceDraft
{
    public required string Role { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetKey { get; init; }
    public required string ValuesJson { get; init; }
}
