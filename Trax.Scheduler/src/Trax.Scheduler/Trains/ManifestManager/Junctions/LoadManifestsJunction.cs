using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Trains.ManifestManager.Junctions;

/// <summary>
/// Projects all enabled manifests into lightweight <see cref="ManifestDispatchView"/> records
/// with pre-computed aggregate flags (FailedCount, HasAwaitingDeadLetter, etc.).
/// </summary>
/// <remarks>
/// This replaces the previous eager-loading approach that used .Include() on Metadatas,
/// DeadLetters, and WorkQueues. Those collections are unbounded and grow with every
/// scheduled execution, causing increasing memory and query cost over time.
///
/// The projection pushes aggregation into the database via COUNT/EXISTS subqueries,
/// keeping the query cost O(manifests) regardless of child table sizes.
/// </remarks>
internal class LoadManifestsJunction(IDataContext dataContext, SchedulerConfiguration config)
    : EffectJunction<Unit, List<ManifestDispatchView>>
{
    public override async Task<List<ManifestDispatchView>> Run(Unit input)
    {
        // A failed run counts toward the backoff and the dead letter only while it is recent:
        // inside the manifest's own failure window when it has one, the scheduler's otherwise.
        var now = DateTime.UtcNow;
        var failureWindowStart = now - config.FailureCountWindow;

        return await dataContext
            .Manifests.Where(m => m.IsEnabled)
            .Select(m => new ManifestDispatchView
            {
                Manifest = m,
                ManifestGroup = m.ManifestGroup,
                // Each condition below is one reason a failed run counts; add a new one as
                // another `&&` term.
                FailedCount = m.Metadatas.Count(md =>
                    md.TrainState == TrainState.Failed
                    // Started inside the failure count window.
                    && md.StartTime
                        >= (
                            m.FailureWindowSeconds == null
                                ? failureWindowStart
                                : now.AddSeconds(-(double)m.FailureWindowSeconds.Value)
                        )
                    // Not a dispatch attempt that was requeued: only one delivery failed, not
                    // the job (see DispatchFailure).
                    && md.FailureException != DispatchFailure.Requeued
                    // Started after the latest resolved (retried or acknowledged) dead letter.
                    && !m.DeadLetters.Any(dl =>
                        (
                            dl.Status == DeadLetterStatus.Retried
                            || dl.Status == DeadLetterStatus.Acknowledged
                        )
                        && dl.ResolvedAt != null
                        && md.StartTime <= dl.ResolvedAt
                    )
                ),
                // Whether the latest run that finished (succeeded, failed or was cancelled) failed.
                // A requeued dispatch attempt is not a finished run, as it is not a failure above.
                LatestFinishedRunFailed = m
                    .Metadatas.Where(md =>
                        md.TrainState == TrainState.Completed
                        || md.TrainState == TrainState.Cancelled
                        || (
                            md.TrainState == TrainState.Failed
                            && md.FailureException != DispatchFailure.Requeued
                        )
                    )
                    .OrderByDescending(md => md.StartTime)
                    .ThenByDescending(md => md.Id)
                    .Select(md => md.TrainState == TrainState.Failed)
                    .FirstOrDefault(),
                LastCancelledRun = m
                    .Metadatas.Where(md => md.TrainState == TrainState.Cancelled)
                    .Max(md => (DateTime?)(md.EndTime ?? md.StartTime)),
                HasAwaitingDeadLetter = m.DeadLetters.Any(dl =>
                    dl.Status == DeadLetterStatus.AwaitingIntervention
                ),
                HasQueuedWork = m.WorkQueues.Any(q => q.Status == WorkQueueStatus.Queued),
                HasActiveExecution = m.Metadatas.Any(md =>
                    md.TrainState == TrainState.Pending || md.TrainState == TrainState.InProgress
                ),
                HasSuccessfulMetadata = m.Metadatas.Any(md =>
                    md.TrainState == TrainState.Completed
                ),
                // A dependent's runs are dated by when the dispatcher dispatched them, which the
                // database's clock stamps, as it stamps the parent's LastSuccessfulRun (see
                // DatabaseClock). A run with no dispatched entry (run directly, or dispatched in
                // memory) falls back to its own start time.
                LatestSuccessfulRunStart =
                    m.ScheduleType == ScheduleType.Dependent
                        ? m.WorkQueues.Where(q =>
                                q.DispatchedAt != null
                                && q.Metadata != null
                                && q.Metadata.TrainState == TrainState.Completed
                            )
                            .Max(q => q.DispatchedAt)
                            ?? m.Metadatas.Where(md => md.TrainState == TrainState.Completed)
                                .Max(md => (DateTime?)md.StartTime)
                        : null,
                LatestCancelledRunStart =
                    m.ScheduleType == ScheduleType.Dependent
                        ? m.WorkQueues.Where(q =>
                                q.DispatchedAt != null
                                && q.Metadata != null
                                && q.Metadata.TrainState == TrainState.Cancelled
                            )
                            .Max(q => q.DispatchedAt)
                            ?? m.Metadatas.Where(md => md.TrainState == TrainState.Cancelled)
                                .Max(md => (DateTime?)md.StartTime)
                        : null,
            })
            .AsNoTracking()
            .ToListAsync(CancellationToken);
    }
}
