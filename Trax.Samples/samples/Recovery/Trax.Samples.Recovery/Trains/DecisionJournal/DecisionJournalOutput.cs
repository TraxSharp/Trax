namespace Trax.Samples.Recovery.Trains.DecisionJournal;

public record DecisionJournalOutput
{
    public required string RunId { get; init; }

    /// <summary>The run this execution was queued to replay, or null.</summary>
    public long? ReplayDecisionsOf { get; init; }

    /// <summary>True when it was queued to replay and asked afresh because the replay could not be honoured.</summary>
    public bool ReplayAbandoned { get; init; }

    public required IReadOnlyList<RecordedDecisionView> Decisions { get; init; }
}
