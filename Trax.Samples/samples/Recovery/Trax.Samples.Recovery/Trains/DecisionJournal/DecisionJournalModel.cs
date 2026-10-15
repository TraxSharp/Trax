namespace Trax.Samples.Recovery.Trains.DecisionJournal;

public record DecisionJournalInput
{
    /// <summary>The run's external id, as runProgress names it. Only the caller who started the run may read it.</summary>
    public required string RunId { get; init; }
}

public record DecisionJournalOutput
{
    public required string RunId { get; init; }

    /// <summary>The run this execution was queued to replay, or null.</summary>
    public long? ReplayDecisionsOf { get; init; }

    /// <summary>True when it was queued to replay and asked afresh because the replay could not be honoured.</summary>
    public bool ReplayAbandoned { get; init; }

    public required IReadOnlyList<RecordedDecisionView> Decisions { get; init; }
}

public record RecordedDecisionView
{
    public required string QuestionKey { get; init; }
    public required int Occurrence { get; init; }
    public required string Kind { get; init; }
    public required bool Replayed { get; init; }

    /// <summary>Why an earlier run's answer was not replayed and the model was asked afresh.</summary>
    public string? ReplayRefused { get; init; }

    public string? Model { get; init; }

    /// <summary>The decider's type name, without its namespace, or null when the answer was replayed.</summary>
    public string? Decider { get; init; }

    /// <summary>The question as Trax put it to the decider, as JSON: its words and each option's description.</summary>
    public required string Question { get; init; }

    /// <summary>The answer the run acted on, as JSON, as the decider gave it or as it was replayed.</summary>
    public string? Answer { get; init; }

    /// <summary>The first characters of the state hash: <c>k1:</c> keyed, <c>s1:</c> unkeyed.</summary>
    public string? StateHash { get; init; }

    public required DateTime DecidedAt { get; init; }
}
