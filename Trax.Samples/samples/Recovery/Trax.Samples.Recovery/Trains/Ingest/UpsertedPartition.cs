using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>What the upsert wrote for the partition.</summary>
public sealed record UpsertedPartition(
    string Source,
    string Month,
    bool Unsure,
    IReadOnlyList<UpsertedWork> Works
);
