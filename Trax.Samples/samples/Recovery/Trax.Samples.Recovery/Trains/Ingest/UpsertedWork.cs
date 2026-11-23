using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>What was written for one work.</summary>
public sealed record UpsertedWork(
    string SourceId,
    string ContentHash,
    string Resolution,
    string? ExistingWorkId
);
