using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

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
