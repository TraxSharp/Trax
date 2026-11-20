using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>A work of the partition whose title is close to one the corpus already holds.</summary>
public sealed record WorkMatch(
    string SourceId,
    string ExistingWorkId,
    string ExistingTitle,
    double Similarity
);
