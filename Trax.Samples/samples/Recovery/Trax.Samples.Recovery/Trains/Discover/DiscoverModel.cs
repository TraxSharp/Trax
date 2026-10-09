using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Discover;

/// <summary>Which source to list the partitions of; every source when left out.</summary>
public sealed record DiscoverPartitionsInput : IManifestProperties
{
    public string? Source { get; init; }
}

/// <summary>One partition of an index: a source, a month as <c>yyyy-MM</c>, and how many records it holds.</summary>
public sealed record SourcePartition(string Source, string Month, int Records);

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
