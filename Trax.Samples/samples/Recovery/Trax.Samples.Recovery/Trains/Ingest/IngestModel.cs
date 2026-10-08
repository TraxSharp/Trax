using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>
/// One partition to ingest: a source and a month, as <c>yyyy-MM</c>. Nothing else, so the same
/// partition always has the same input.
/// </summary>
public sealed record IngestPartitionInput(string Source, string Month) : IManifestProperties;

/// <summary>The partition's records, exactly as the index sent them.</summary>
public sealed record FetchedPartition(
    string Source,
    string Month,
    IReadOnlyList<FetchedRecord> Records
);

/// <summary>One record's id and its JSON, in the index's own shape.</summary>
public sealed record FetchedRecord(string SourceId, string Payload);

/// <summary>A work in one shape whichever index it came from.</summary>
public sealed record NormalisedWork(
    string SourceId,
    string? Doi,
    string Title,
    string Abstract,
    int Year,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> References,
    string ContentHash
);

/// <summary>The partition's works, normalised, in source id order.</summary>
public sealed record NormalisedPartition(
    string Source,
    string Month,
    IReadOnlyList<NormalisedWork> Works
);

/// <summary>A work of the partition whose title is close to one the corpus already holds.</summary>
public sealed record WorkMatch(
    string SourceId,
    string ExistingWorkId,
    string ExistingTitle,
    double Similarity
);

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

/// <summary>A work and what was decided about it.</summary>
public sealed record ResolvedWork(NormalisedWork Work, string Resolution, string? ExistingWorkId);

/// <summary>
/// Every work of the partition with what was decided about it. <see cref="Unsure"/> is set when
/// the model could not say whether the close matches are the same works.
/// </summary>
public sealed record ResolvedPartition(
    string Source,
    string Month,
    bool Unsure,
    IReadOnlyList<ResolvedWork> Works
);

/// <summary>What was written for one work.</summary>
public sealed record UpsertedWork(
    string SourceId,
    string ContentHash,
    string Resolution,
    string? ExistingWorkId
);

/// <summary>What the upsert wrote for the partition.</summary>
public sealed record UpsertedPartition(
    string Source,
    string Month,
    bool Unsure,
    IReadOnlyList<UpsertedWork> Works
);

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

[Asks(
    "Are the works in this partition that look like works the corpus already holds the same works?",
    Yes = "Merge them: each is the same paper as the corpus work it matches.",
    No = "Keep them apart: the titles are alike but the papers are different."
)]
public sealed class SameWorkAsExisting;
