using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>The partition's works, normalised, in source id order.</summary>
public sealed record NormalisedPartition(
    string Source,
    string Month,
    IReadOnlyList<NormalisedWork> Works
);
