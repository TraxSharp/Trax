namespace Trax.Samples.Recovery.Trains.PartitionAction;

/// <summary>Where the instance is after the action, or why it did not move.</summary>
public sealed record PartitionActionOutput
{
    /// <summary>The state the instance moved to; null when it did not move.</summary>
    public string? State { get; init; }

    /// <summary>Why the instance did not move (<c>NO_TRANSITION</c>, <c>NOT_FOUND</c>, ...); null when it did.</summary>
    public string? Problem { get; init; }
}
