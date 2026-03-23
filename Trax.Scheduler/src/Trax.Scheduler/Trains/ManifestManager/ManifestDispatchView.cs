using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;

namespace Trax.Scheduler.Trains.ManifestManager;

/// <summary>
/// Lightweight projection of a Manifest with pre-computed aggregate flags.
/// Avoids eagerly loading unbounded child collections (Metadatas, DeadLetters, WorkQueues).
/// </summary>
internal record ManifestDispatchView
{
    public required Manifest Manifest { get; init; }
    public required ManifestGroup ManifestGroup { get; init; }
    public required int FailedCount { get; init; }
    public required bool HasAwaitingDeadLetter { get; init; }
    public required bool HasQueuedWork { get; init; }
    public required bool HasActiveExecution { get; init; }
    public required bool HasSuccessfulMetadata { get; init; }
}
