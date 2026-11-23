using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Discover;

/// <summary>
/// The train's output: every partition found, in source and month order, and what this run did about
/// their machine instances.
/// </summary>
/// <param name="Partitions">Every partition found.</param>
public sealed record DiscoveredPartitions(IReadOnlyList<SourcePartition> Partitions)
{
    /// <summary>How many partition instances this run created; none when they all existed already.</summary>
    public int InstancesStarted { get; init; }

    /// <summary>How many instances this run sent from <c>Discovered</c> into <c>Ingesting</c>.</summary>
    public int InstancesSentToIngest { get; init; }
}
