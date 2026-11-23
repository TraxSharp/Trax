using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>
/// The train's output: counts and a fingerprint, never the works themselves. The works are in
/// <c>topic_map.ingested_works</c>; this says how many there are and whether they changed.
/// </summary>
/// <param name="Works">How many works the partition holds.</param>
/// <param name="Created">How many were new to the corpus.</param>
/// <param name="Merged">How many were linked to a work the corpus holds.</param>
/// <param name="NeedsReview">How many are kept aside for a person to decide.</param>
/// <param name="Unsure">Whether the model was unsure, so some works need review.</param>
/// <param name="Fingerprint">
/// A SHA-256 over what was written, the same for the same content and resolutions.
/// </param>
public sealed record IngestPartitionResult(
    string Source,
    string Month,
    int Works,
    int Created,
    int Merged,
    int NeedsReview,
    bool Unsure,
    string Fingerprint
);
