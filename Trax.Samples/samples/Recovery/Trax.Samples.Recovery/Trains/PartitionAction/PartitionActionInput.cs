namespace Trax.Samples.Recovery.Trains.PartitionAction;

public sealed record PartitionActionInput
{
    public required string Source { get; init; }

    /// <summary>The partition's month, as <c>yyyy-MM</c>.</summary>
    public required string Month { get; init; }

    public required PartitionAction Action { get; init; }
}
