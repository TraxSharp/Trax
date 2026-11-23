namespace Trax.Samples.Recovery.Trains.PartitionAction;

/// <summary>What an operator can ask of a partition's instance.</summary>
public enum PartitionAction
{
    /// <summary>Run the ingest again, from <c>Failed</c> or <c>Cancelled</c>: a new run.</summary>
    Retry,

    /// <summary>Let a partition held for review through, from <c>NeedsReview</c>.</summary>
    Approve,
}
