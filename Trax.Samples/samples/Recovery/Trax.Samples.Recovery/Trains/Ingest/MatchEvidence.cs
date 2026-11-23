using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>
/// What the model is asked about: the partition's works and, for those that look like a work the
/// corpus holds, the closest one and how close it is.
/// </summary>
public sealed record MatchEvidence(
    string Source,
    string Month,
    IReadOnlyList<NormalisedWork> Works,
    IReadOnlyList<WorkMatch> Candidates
);
