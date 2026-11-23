using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

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
