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

    /// <summary>
    /// Failed runs that count toward the retry backoff and the dead letter: those started within
    /// the manifest's own failure window (<c>SchedulerConfiguration.FailureCountWindow</c> when it
    /// has none) and after the latest resolved dead letter.
    /// </summary>
    public required int FailedCount { get; init; }

    /// <summary>
    /// Whether the manifest's latest finished run (succeeded, failed or cancelled) failed. Only
    /// then is the next run a retry, delayed by the backoff; after a success or a cancel the next
    /// occurrence runs on time, however many failures are still in the window.
    /// </summary>
    public bool LatestFinishedRunFailed { get; init; }

    /// <summary>
    /// When the manifest's most recent cancelled run ended (its start time if it has no end time),
    /// or <c>null</c> when it has none. A cancelled run consumes its occurrence, so the schedule is
    /// evaluated from whichever is later: this or <c>Manifest.LastSuccessfulRun</c>.
    /// </summary>
    public DateTime? LastCancelledRun { get; init; }
    public required bool HasAwaitingDeadLetter { get; init; }
    public required bool HasQueuedWork { get; init; }
    public required bool HasActiveExecution { get; init; }
    public required bool HasSuccessfulMetadata { get; init; }

    /// <summary>
    /// When the manifest's latest successful run started, loaded for dependents only (null for
    /// every other schedule type, and when no successful run is on record).
    /// </summary>
    /// <remarks>
    /// A dependent is due when its parent succeeded after this: a parent success that lands
    /// while the dependent is running was not seen by that run, so it earns another.
    /// </remarks>
    public DateTime? LatestSuccessfulRunStart { get; init; }

    /// <summary>
    /// When the manifest's latest cancelled run started, loaded for dependents only (null for
    /// every other schedule type, and when no cancelled run is on record).
    /// </summary>
    /// <remarks>
    /// A cancelled dependent run consumed the parent success it was started for, and only that
    /// one: a parent success that landed after it started, even before it was cancelled, was not
    /// seen by it, so the baseline is when it started, as for a successful run. The scheduled
    /// schedule types use <see cref="LastCancelledRun"/>, the run's end, instead.
    /// </remarks>
    public DateTime? LatestCancelledRunStart { get; init; }
}
