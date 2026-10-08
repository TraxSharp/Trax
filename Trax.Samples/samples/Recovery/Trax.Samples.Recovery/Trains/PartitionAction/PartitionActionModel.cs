namespace Trax.Samples.Recovery.Trains.PartitionAction;

/// <summary>What an operator can ask of a partition's instance.</summary>
public enum PartitionAction
{
    /// <summary>Run the ingest again, from <c>Failed</c> or <c>Cancelled</c>: a new run.</summary>
    Retry,

    /// <summary>Let a partition held for review through, from <c>NeedsReview</c>.</summary>
    Approve,
}

public sealed record PartitionActionInput
{
    public required string Source { get; init; }

    /// <summary>The partition's month, as <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    public required PartitionAction Action { get; init; }
}

/// <summary>Where the instance is after the action, or why it did not move.</summary>
public sealed record PartitionActionOutput
{
    /// <summary>The state the instance moved to; null when it did not move.</summary>
    public string? State { get; init; }

    /// <summary>Why the instance did not move (<c>no-transition</c>, <c>not-found</c>, ...); null when it did.</summary>
    public string? Problem { get; init; }
}
