using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>The partition's records, exactly as the index sent them.</summary>
public sealed record FetchedPartition(
    string Source,
    string Month,
    IReadOnlyList<FetchedRecord> Records
);
