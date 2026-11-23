using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.Checkpoints;
using Trax.Mediator.Services.ChainVerification;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

/// <summary>
/// Chooses the run a manifest's retry resumes from a checkpoint: the manifest's failed run, when
/// it has a checkpoint the train's declared chain can resume after. See
/// Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.
/// </summary>
/// <remarks>
/// <para>
/// The ManifestManager's retry and a dead-letter requeue use it, whatever the manifest says about
/// replaying decisions: declaring a checkpoint is the opt-in. The source is read from the database
/// here, never from anything a caller supplies, with the lookup a retry's replay makes: the
/// manifest's latest finished run, failed, a run of the manifest's train that no state machine
/// invoked, dispatched from an entry of the manifest with no subject key and exactly the input
/// the retry is queued with. A dependent whose parent has succeeded again since is a new firing,
/// not a retry, and reruns. Anything else, and a run <see cref="IRunResumes"/> says cannot resume
/// after its latest checkpoint, returns no source and the retry reruns the chain from the top.
/// </para>
/// <para>
/// The lookup itself failing is logged and never fails the caller: rerunning from the top is
/// always safe. It reads through a short-lived context of its own.
/// </para>
/// </remarks>
internal class RetryResume(
    IDataContextProviderFactory contextFactory,
    IRunResumes? resumes,
    ITrainChainGraphs? chains,
    ILogger logger
)
{
    /// <summary>
    /// The run each manifest's retry resumes, by manifest id. A manifest missing from the result
    /// reruns from the top. Every manifest is assumed to be retried with its stored properties.
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<long, long>> SourcesForRetriesAsync(
        IReadOnlyCollection<Manifest> manifests,
        CancellationToken ct
    )
    {
        if (manifests.Count == 0 || resumes is null || chains is null)
            return new Dictionary<long, long>();

        try
        {
            return await LookUpAsync(manifests, resumes, chains, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                e,
                "Could not look up the runs {Count} manifest retries resume; they rerun from the "
                    + "top",
                manifests.Count
            );
            return new Dictionary<long, long>();
        }
    }

    /// <summary>The run <paramref name="manifest"/>'s retry resumes, or null when it reruns.</summary>
    public async Task<long?> SourceForRetryAsync(Manifest manifest, CancellationToken ct) =>
        (await SourcesForRetriesAsync([manifest], ct).ConfigureAwait(false)).TryGetValue(
            manifest.Id,
            out var source
        )
            ? source
            : null;

    private async Task<IReadOnlyDictionary<long, long>> LookUpAsync(
        IReadOnlyCollection<Manifest> manifests,
        IRunResumes resumes,
        ITrainChainGraphs chains,
        CancellationToken ct
    )
    {
        var sources = new Dictionary<long, long>();
        var candidates = await FindCandidatesAsync(manifests, ct).ConfigureAwait(false);

        // Asked after the context above is gone: each check reads the run's lineage itself.
        foreach (var (manifest, runId) in candidates)
        {
            if (chains.FindDeclared(manifest.Name) is not { } declared)
            {
                logger.LogInformation(
                    "The retry of manifest {ManifestId} reruns from the top: the chain of "
                        + "{TrainName} cannot be read on this host to check a resume of run "
                        + "{FailedRun}",
                    manifest.Id,
                    manifest.Name,
                    runId
                );
                continue;
            }

            var verdict = await resumes
                .Check(
                    declared.Train,
                    declared.Chain,
                    declared.Input,
                    declared.Output,
                    runId,
                    resumeAt: null,
                    ct
                )
                .ConfigureAwait(false);

            if (verdict.CanResume)
                sources[manifest.Id] = runId;
            else
                logger.LogInformation(
                    "The retry of manifest {ManifestId} reruns from the top instead of resuming "
                        + "run {FailedRun}: {Reason}",
                    manifest.Id,
                    runId,
                    verdict.Reason
                );
        }

        return sources;
    }

    // The manifests whose failed run may be resumed, with that run, read through a context that
    // is gone before any resume is checked.
    private async Task<List<(Manifest Manifest, long RunId)>> FindCandidatesAsync(
        IReadOnlyCollection<Manifest> manifests,
        CancellationToken ct
    )
    {
        var candidates = new List<(Manifest Manifest, long RunId)>();

        var context = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var manifestIds = manifests.Select(m => m.Id).ToList();

            var latestByManifest = await ReadLatestFinishedAsync(context, manifestIds, ct)
                .ConfigureAwait(false);

            var latestIds = latestByManifest
                .Where(l => l.Latest is not null)
                .Select(l => l.Latest.NotNull().Value)
                .ToList();

            if (latestIds.Count == 0)
                return candidates;

            var runs = await ReadResumableRunsAsync(context, latestIds, ct).ConfigureAwait(false);

            if (runs.Count == 0)
                return candidates;

            var runIds = runs.Select(r => r.Id).ToList();
            var entries = await ReadEntriesAsync(context, runIds, ct).ConfigureAwait(false);

            var parentSuccess = await ReadParentSuccessAsync(context, manifests, ct)
                .ConfigureAwait(false);

            var runById = runs.ToDictionary(r => r.Id);
            var latestById = latestByManifest.ToDictionary(l => l.Id, l => l.Latest);

            foreach (var manifest in manifests)
            {
                if (
                    latestById.GetValueOrDefault(manifest.Id) is not { } runId
                    || !runById.TryGetValue(runId, out var run)
                )
                    continue; // No failed run with a checkpoint: the retry reruns.

                entries.TryGetValue(runId, out var entry);

                var why = WhyReruns(manifest, run, entry, parentSuccess);

                if (why is not null)
                {
                    LogRerunsFromTop(manifest.Id, runId, why);
                    continue;
                }

                candidates.Add((manifest, runId));
            }
        }

        return candidates;
    }

    // A manifest's latest finished run, and a failed run and its entry, as the lookup reads them.
    private sealed record LatestRun(long Id, long? Latest);

    private sealed record ResumableRun(
        long Id,
        string Name,
        string? InvokingMachine,
        DateTime StartTime
    );

    private sealed record RunEntry(
        long MetadataId,
        long? ManifestId,
        string? Input,
        string? InputTypeName,
        string? SubjectKey,
        DateTime? DispatchedAt
    );

    private static async Task<List<LatestRun>> ReadLatestFinishedAsync(
        IDataContext context,
        List<long> manifestIds,
        CancellationToken ct
    )
    {
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
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return latestByManifest.Select(l => new LatestRun(l.Id, l.Latest)).ToList();
    }

    private static async Task<List<ResumableRun>> ReadResumableRunsAsync(
        IDataContext context,
        List<long> latestIds,
        CancellationToken ct
    )
    {
        // Only a failed run with a checkpoint of its own, or one that resumed another (whose
        // checkpoints it counts), can resume; the rest are not worth asking about.
        var runs = await context
            .Metadatas.AsNoTracking()
            .Where(m =>
                latestIds.Contains(m.Id)
                && m.TrainState == TrainState.Failed
                && (m.ResumeFrom != null || context.Checkpoints.Any(c => c.MetadataId == m.Id))
            )
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.InvokingMachine,
                m.StartTime,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return runs.Select(r => new ResumableRun(r.Id, r.Name, r.InvokingMachine, r.StartTime))
            .ToList();
    }

    private static async Task<Dictionary<long, RunEntry>> ReadEntriesAsync(
        IDataContext context,
        List<long> runIds,
        CancellationToken ct
    )
    {
        var entries = (
            await context
                .WorkQueues.AsNoTracking()
                .Where(q => q.MetadataId != null && runIds.Contains(q.MetadataId.Value))
                .Select(q => new
                {
                    MetadataId = q.MetadataId.NotNull().Value,
                    q.ManifestId,
                    q.Input,
                    q.InputTypeName,
                    q.SubjectKey,
                    q.DispatchedAt,
                })
                .ToListAsync(ct)
                .ConfigureAwait(false)
        ).GroupBy(q => q.MetadataId).ToDictionary(g => g.Key, g => g.First());

        return entries.ToDictionary(
            e => e.Key,
            e => new RunEntry(
                e.Value.MetadataId,
                e.Value.ManifestId,
                e.Value.Input,
                e.Value.InputTypeName,
                e.Value.SubjectKey,
                e.Value.DispatchedAt
            )
        );
    }

    private static async Task<Dictionary<long, DateTime?>> ReadParentSuccessAsync(
        IDataContext context,
        IReadOnlyCollection<Manifest> manifests,
        CancellationToken ct
    )
    {
        var parentIds = manifests
            .Where(m => m.DependsOnManifestId is not null)
            .Select(m => (long)m.DependsOnManifestId.NotNull().Value)
            .Distinct()
            .ToList();
        var parentSuccess =
            parentIds.Count == 0
                ? new Dictionary<long, DateTime?>()
                : await context
                    .Manifests.AsNoTracking()
                    .Where(m => parentIds.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, m => m.LastSuccessfulRun, ct)
                    .ConfigureAwait(false);

        return parentSuccess;
    }

    // Why a manifest's retry reruns from the top rather than resuming its failed run, or null
    // when it may resume it.
    private static string? WhyReruns(
        Manifest manifest,
        ResumableRun run,
        RunEntry? entry,
        Dictionary<long, DateTime?> parentSuccess
    )
    {
        return !string.Equals(run.Name, manifest.Name, StringComparison.Ordinal)
                ? "it is not a run of this manifest's train"
            : run.InvokingMachine is not null
                ? "a state machine invoked it, and only the machine retries it"
            : manifest.DependsOnManifestId is { } parent
            && parentSuccess.GetValueOrDefault(parent) is { } succeeded
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
    }

    private void LogRerunsFromTop(long manifestId, long runId, string why) =>
        logger.LogInformation(
            "The retry of manifest {ManifestId} reruns from the top instead of "
                + "resuming run {FailedRun}: {Reason}",
            manifestId,
            runId,
            why
        );
}
