using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Discover;

/// <summary>Which source to list the partitions of; every source when left out.</summary>
public sealed record DiscoverPartitionsInput : IManifestProperties
{
    public string? Source { get; init; }
}
