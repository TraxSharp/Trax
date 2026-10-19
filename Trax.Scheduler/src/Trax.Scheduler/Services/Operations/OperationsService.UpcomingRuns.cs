using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Services.Operations;

// When each manifest runs next, read from the manifest manager's own due-ness code
// (SchedulingHelpers.ShouldRunNow) so the console and the manager cannot disagree.
public partial class OperationsService
{
    /// <summary>The most upcoming runs <see cref="GetUpcomingRunsAsync"/> lists per manifest.</summary>
    public const int MaxUpcomingRuns = SchedulingHelpers.MaxUpcomingRuns;

    /// <inheritdoc />
    /// <remarks>
    /// One query for the batch: the manifest's own row, its group's and its parent's enabled flags,
    /// whether a dead letter awaits intervention, and its latest cancelled run, the last served by
    /// <c>ix_metadata_manifest_state</c>. The times are computed in memory.
    /// </remarks>
    public async Task<IReadOnlyList<ManifestUpcomingRuns>> GetUpcomingRunsAsync(
        IReadOnlyCollection<long> manifestIds,
        int count,
        CancellationToken ct
    )
    {
        var ids = manifestIds.Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<ManifestUpcomingRuns>();

        if (ids.Length > MaxBatchSize)
            throw new ArgumentOutOfRangeException(
                nameof(manifestIds),
                ids.Length,
                $"At most {MaxBatchSize} manifest ids can be given at once."
            );

        count = Math.Clamp(count, 0, MaxUpcomingRuns);

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        var rows = await db
            .Manifests.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new
            {
                m.Id,
                m.IsEnabled,
                m.ScheduleType,
                m.CronExpression,
                m.IntervalSeconds,
                m.MisfirePolicy,
                m.MisfireThresholdSeconds,
                m.ScheduledAt,
                m.Exclusions,
                m.LastSuccessfulRun,
                m.NextScheduledRun,
                m.VarianceSeconds,
                m.DependsOnManifestId,
                GroupEnabled = m.ManifestGroup.IsEnabled,
                ParentExternalId = m.DependsOnManifest == null
                    ? null
                    : m.DependsOnManifest.ExternalId,
                ParentEnabled = m.DependsOnManifest != null && m.DependsOnManifest.IsEnabled,
                HasAwaitingDeadLetter = m.DeadLetters.Any(dl =>
                    dl.Status == DeadLetterStatus.AwaitingIntervention
                ),
                // As LoadManifestsJunction reads it for the manager.
                LastCancelledRun = m
                    .Metadatas.Where(md => md.TrainState == TrainState.Cancelled)
                    .Max(md => (DateTime?)(md.EndTime ?? md.StartTime)),
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var byId = rows.ToDictionary(r => r.Id);

        return ids.Where(byId.ContainsKey)
            .Select(id =>
            {
                var r = byId[id];
                var manifest = new Effect.Models.Manifest.Manifest
                {
                    IsEnabled = r.IsEnabled,
                    ScheduleType = r.ScheduleType,
                    CronExpression = r.CronExpression,
                    IntervalSeconds = r.IntervalSeconds,
                    MisfirePolicy = r.MisfirePolicy,
                    MisfireThresholdSeconds = r.MisfireThresholdSeconds,
                    ScheduledAt = r.ScheduledAt,
                    Exclusions = r.Exclusions,
                    LastSuccessfulRun = r.LastSuccessfulRun,
                    NextScheduledRun = r.NextScheduledRun,
                    VarianceSeconds = r.VarianceSeconds,
                    DependsOnManifestId = r.DependsOnManifestId,
                };

                // The manager skips a manifest that is disabled, sits in a disabled group, or
                // waits on an operator's answer to its dead letter.
                var canRun = r.IsEnabled && r.GroupEnabled && !r.HasAwaitingDeadLetter;
                var runsAfter =
                    canRun && r.ScheduleType == ScheduleType.Dependent && r.ParentEnabled
                        ? r.DependsOnManifestId
                        : null;

                return new ManifestUpcomingRuns(
                    id,
                    ScheduleText.Describe(manifest, r.ParentExternalId),
                    canRun
                        ? SchedulingHelpers.UpcomingRuns(
                            manifest,
                            count,
                            now,
                            _schedulerConfiguration,
                            r.LastCancelledRun
                        )
                        : Array.Empty<DateTime>(),
                    runsAfter,
                    runsAfter is null ? null : r.ParentExternalId
                );
            })
            .ToList();
    }
}
