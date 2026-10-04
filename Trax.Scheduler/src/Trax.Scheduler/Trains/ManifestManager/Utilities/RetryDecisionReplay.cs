using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

/// <summary>
/// Chooses the run a manifest's retry replays the decisions of: the manifest's failed run, when
/// replaying its answers is sound. See <c>docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// The source is read from the database here and nowhere else, never from anything a caller
/// supplies. The ManifestManager's retry and a dead-letter requeue use it; an ordinary occurrence
/// (the latest finished run succeeded or was cancelled) replays nothing.
/// </para>
/// <para>
/// A run's answers are replayed at most once. The source must be a run that asked its deciders
/// itself and whose answers nothing else replays, in any state: a failed run that was itself a
/// replay, or one another run or a queued entry already replays, is retried afresh, so one bad
/// answer cannot hold a manifest in a loop of retries, dead letters and requeues that all repeat
/// it. Because the source never replays another run, the replay reads its answers alone and there
/// is no chain to follow. Metadata cleanup never deletes a run that replays another while it keeps
/// the run replayed, so the test sees every replay of the source on any host, whatever retention
/// that host is configured with. A dependent's run asks afresh once its parent has succeeded again
/// after the failed run was dispatched, which makes the next run a new firing rather than a retry.
/// </para>
/// <para>
/// The source must be a failed run of the manifest's train that recorded its decisions and acted
/// on at least one, and the work queue entry it was dispatched from must belong to the manifest,
/// carry no subject key, and hold exactly the input and input type the retry is queued with,
/// compared ordinally as stored. Anything else returns no source and the retry asks afresh; that
/// includes the lookup itself failing, which is logged and never fails the caller.
/// </para>
/// <para>
/// The lookup runs on a short-lived context of its own, so it never reads or writes through the
/// caller's context or transaction, and it is set-based: a page of manifests costs a fixed number
/// of queries.
/// </para>
/// </remarks>
internal class RetryDecisionReplay(IDataContextProviderFactory contextFactory, ILogger logger)
{
    /// <summary>
    /// The run each manifest's retry replays, by manifest id. A manifest missing from the result
    /// asks afresh. Every manifest is assumed to be retried with its stored properties.
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<long, long>> SourcesForRetriesAsync(
        IReadOnlyCollection<Manifest> manifests,
        CancellationToken ct
    )
    {
        var replaying = new List<Manifest>();
        foreach (var manifest in manifests)
        {
            if (manifest.ReplayDecisionsOnRetry)
                replaying.Add(manifest);
            else
                logger.LogDebug(
                    "The retry of manifest {ManifestId} asks its deciders afresh: the manifest "
                        + "does not replay decisions on retry",
                    manifest.Id
                );
        }

        if (replaying.Count == 0)
            return new Dictionary<long, long>();

        try
        {
            return await LookUpAsync(replaying, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Asking afresh is always safe; failing the retry, or the cycle that queues it, is not.
            logger.LogWarning(
                e,
                "Could not look up the runs to replay for {Count} manifest retries; they ask "
                    + "their deciders afresh",
                replaying.Count
            );
            return new Dictionary<long, long>();
        }
    }

    /// <summary>The run <paramref name="manifest"/>'s retry replays, or null when it asks afresh.</summary>
    public async Task<long?> SourceForRetryAsync(Manifest manifest, CancellationToken ct) =>
        (await SourcesForRetriesAsync([manifest], ct)).TryGetValue(manifest.Id, out var source)
            ? source
            : null;

    private async Task<IReadOnlyDictionary<long, long>> LookUpAsync(
        List<Manifest> manifests,
        CancellationToken ct
    )
    {
        using var context = await contextFactory.CreateDbContextAsync(ct);

        var manifestIds = manifests.Select(m => m.Id).ToList();

        // The latest run that finished, chosen as LoadManifestsJunction chooses it.
        var latestByManifest = await context
            .Manifests.AsNoTracking()
            .Where(m => manifestIds.Contains(m.Id))
            .Select(m => new
            {
                m.Id,
                Latest = m
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
                    .Select(md => (long?)md.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var latestIds = latestByManifest
            .Where(l => l.Latest is not null)
            .Select(l => l.Latest!.Value)
            .ToList();

        if (latestIds.Count == 0)
            return new Dictionary<long, long>();

        var runs = await context
            .Metadatas.AsNoTracking()
            .Where(m => latestIds.Contains(m.Id) && m.TrainState == TrainState.Failed)
            .Select(m => new
            {
                m.Id,
                m.ManifestId,
                m.Name,
                m.DecisionsRecorded,
                m.ReplayDecisionsOf,
                m.ReplayAbandoned,
                m.StartTime,
            })
            .ToListAsync(ct);

        var runIds = runs.Select(r => r.Id).ToList();

        var entries = (
            await context
                .WorkQueues.AsNoTracking()
                .Where(q => q.MetadataId != null && runIds.Contains(q.MetadataId.Value))
                .Select(q => new
                {
                    MetadataId = q.MetadataId!.Value,
                    q.ManifestId,
                    q.Input,
                    q.InputTypeName,
                    q.SubjectKey,
                    q.DispatchedAt,
                })
                .ToListAsync(ct)
        ).GroupBy(q => q.MetadataId).ToDictionary(g => g.Key, g => g.First());

        // Runs whose answers something already replays, in any state: a run queued, running or
        // finished with them (a manifest retry, or a requeue through RequeueExecutionAsync, which
        // belongs to no manifest), or an entry still queued to. Each is the answers' one replay.
        var alreadyReplayed = (
            await context
                .Metadatas.AsNoTracking()
                // A run that abandoned its replay and asked afresh did not replay them.
                .Where(r =>
                    r.ReplayDecisionsOf != null
                    && runIds.Contains(r.ReplayDecisionsOf.Value)
                    && !r.ReplayAbandoned
                )
                .Select(r => r.ReplayDecisionsOf!.Value)
                .Distinct()
                .ToListAsync(ct)
        ).ToHashSet();
        alreadyReplayed.UnionWith(
            await context
                .WorkQueues.AsNoTracking()
                .Where(q =>
                    q.ReplayDecisionsOf != null
                    && runIds.Contains(q.ReplayDecisionsOf.Value)
                    && q.Status == WorkQueueStatus.Queued
                )
                .Select(q => q.ReplayDecisionsOf!.Value)
                .Distinct()
                .ToListAsync(ct)
        );

        // A dependent is fired by its parent's success. Once the parent has succeeded again since
        // the failed run was dispatched, the next run is a new firing, not a retry of that one.
        var parentIds = manifests
            .Where(m => m.DependsOnManifestId is not null)
            .Select(m => (long)m.DependsOnManifestId!.Value)
            .Distinct()
            .ToList();
        var parentSuccess =
            parentIds.Count == 0
                ? new Dictionary<long, DateTime?>()
                : await context
                    .Manifests.AsNoTracking()
                    .Where(m => parentIds.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, m => m.LastSuccessfulRun, ct);

        // A decision the run acted on: a refused answer is never replayed, so it does not count.
        var decided = (
            await context
                .RecordedDecisions.AsNoTracking()
                .Where(d => runIds.Contains(d.MetadataId) && d.Refused == null)
                .Select(d => d.MetadataId)
                .Distinct()
                .ToListAsync(ct)
        ).ToHashSet();

        var runById = runs.ToDictionary(r => r.Id);
        var latestById = latestByManifest.ToDictionary(l => l.Id, l => l.Latest);
        var sources = new Dictionary<long, long>();

        foreach (var manifest in manifests)
        {
            if (
                latestById.GetValueOrDefault(manifest.Id) is not { } runId
                || !runById.TryGetValue(runId, out var run)
            )
                continue; // No failed run to retry: an ordinary occurrence.

            entries.TryGetValue(runId, out var entry);

            string? why =
                run.Name != manifest.Name ? "it is not a run of this manifest's train"
                : !run.DecisionsRecorded ? "it did not record its decisions"
                // A run that abandoned its replay asked its deciders itself, so its answers are
                // its own and may be replayed once, like any run's.
                : run.ReplayDecisionsOf is not null && !run.ReplayAbandoned
                    ? "it replayed another run's answers and failed, so they are not replayed again"
                : alreadyReplayed.Contains(runId)
                    ? "its answers are already being, or have been, replayed once"
                : manifest.DependsOnManifestId is { } parent
                && parentSuccess.GetValueOrDefault(parent) is { } succeeded
                // Both by the database's clock, as LoadManifestsJunction compares them: the
                // parent's LastSuccessfulRun is stamped by it (DatabaseClock), and so is the
                // entry's DispatchedAt. A run with no dispatched entry falls back to its start.
                && succeeded > (entry?.DispatchedAt ?? run.StartTime)
                    ? "its parent succeeded again since, so this run is a new firing, not a retry"
                : entry is null || entry.ManifestId != manifest.Id
                    ? "it has no work queue entry of this manifest to compare inputs with"
                : entry.SubjectKey is not null ? "it was queued under a subject key"
                : !string.Equals(
                    entry.InputTypeName,
                    manifest.PropertyTypeName,
                    StringComparison.Ordinal
                ) || !string.Equals(entry.Input, manifest.Properties, StringComparison.Ordinal)
                    ? "it was given a different input from the one the retry is given"
                : null;

            if (why is not null)
            {
                logger.LogInformation(
                    "The retry of manifest {ManifestId} asks its deciders afresh instead of "
                        + "replaying run {FailedRun}: {Reason}",
                    manifest.Id,
                    runId,
                    why
                );
                continue;
            }

            // A train that never decides is retried plainly, with no link.
            if (decided.Contains(runId))
                sources[manifest.Id] = runId;
        }

        return sources;
    }
}
