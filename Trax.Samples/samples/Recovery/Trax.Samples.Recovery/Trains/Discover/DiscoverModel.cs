using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Discover;

/// <summary>Which source to list the partitions of; every source when left out.</summary>
public sealed record DiscoverPartitionsInput : IManifestProperties
{
    public string? Source { get; init; }
}

/// <summary>One partition of an index: a source, a month as <c>yyyy-MM</c>, and how many records it holds.</summary>
public sealed record SourcePartition(string Source, string Month, int Records);

/// <summary>The train's output: every partition found, in source and month order.</summary>
public sealed record DiscoveredPartitions(IReadOnlyList<SourcePartition> Partitions);
