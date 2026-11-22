using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Metadata;
using Trax.Samples.Scheduling.Services;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.Scheduling.E2E.Utilities;

/// <summary>
/// Drives <c>import-supplier-feed</c> through a fresh outage: three failing calls, so the first
/// run and its two retries fail and the manifest is dead-lettered.
/// </summary>
public static class SupplierOutage
{
    public sealed record Result(
        long ManifestId,
        IReadOnlyList<Metadata> Failures,
        DeadLetter DeadLetter
    );

    public static async Task<Result> RunToDeadLetter(
        Db db,
        SupplierFeed feed,
        ITraxScheduler scheduler
    )
    {
        var manifest = await db.Manifest(ManifestNames.ImportSupplierFeed).ConfigureAwait(false);

        await SettleEarlierOutagesAsync(db, scheduler, manifest.Id).ConfigureAwait(false);

        var lastDeadLetterId =
            await db.Query(dc => dc.DeadLetters.MaxAsync(d => (long?)d.Id)).ConfigureAwait(false)
            ?? 0;
        var marker = await db.LatestMetadataId().ConfigureAwait(false);

        // Only now, so a run the manifest's own schedule starts from here on is after the marker.
        feed.StartOutage(3);

        // The trigger runs it now. Each retry then waits out its backoff from the failure before
        // it (2 s, then 4 s), plus up to a manager and a dispatcher poll, not the manifest's
        // 15-second interval.
        await scheduler.TriggerAsync(ManifestNames.ImportSupplierFeed).ConfigureAwait(false);

        // MaxRetries(2): the first run and two retries.
        var failures = await db.WaitForRuns(
                manifest.Id,
                TrainState.Failed,
                3,
                marker,
                TimeSpan.FromSeconds(60)
            )
            .ConfigureAwait(false);
        var deadLetter = await db.WaitForDeadLetter(
                manifest.Id,
                DeadLetterStatus.AwaitingIntervention,
                TimeSpan.FromSeconds(30),
                lastDeadLetterId
            )
            .ConfigureAwait(false);

        return new Result(manifest.Id, failures, deadLetter);
    }

    // Waits out an earlier test's runs of the manifest and acknowledges its dead letters.
    private static async Task SettleEarlierOutagesAsync(
        Db db,
        ITraxScheduler scheduler,
        long manifestId
    )
    {
        // A run still in flight from an earlier test would count toward this outage's retries.
        await db.WaitUntil(
                () =>
                    db.Query(async dc =>
                        !await dc
                            .Metadatas.AnyAsync(m =>
                                m.ManifestId == manifestId
                                && (
                                    m.TrainState == TrainState.Pending
                                    || m.TrainState == TrainState.InProgress
                                )
                            )
                            .ConfigureAwait(false)
                    ),
                TimeSpan.FromSeconds(30),
                "no run of import-supplier-feed should still be in flight"
            )
            .ConfigureAwait(false);

        // A dead letter still awaiting intervention holds the manifest back. Acknowledging it
        // also resets the failure count, so only this outage's failures count.
        var awaiting = await db.Query(dc =>
                dc.DeadLetters.Where(d =>
                        d.ManifestId == manifestId
                        && d.Status == DeadLetterStatus.AwaitingIntervention
                    )
                    .Select(d => d.Id)
                    .ToListAsync()
            )
            .ConfigureAwait(false);
        if (awaiting.Count > 0)
            await scheduler
                .AcknowledgeDeadLettersAsync([.. awaiting], "Reset by the E2E suite")
                .ConfigureAwait(false);
    }
}
