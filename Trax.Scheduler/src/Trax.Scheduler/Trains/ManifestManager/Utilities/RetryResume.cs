using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
            return await LookUpAsync(manifests, resumes, chains, ct);
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
        (await SourcesForRetriesAsync([manifest], ct)).TryGetValue(manifest.Id, out var source)
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
        var candidates = new List<(Manifest Manifest, long RunId)>();

        using (var context = await contextFactory.CreateDbContextAsync(ct))
        {
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
                return sources;

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
                .ToListAsync(ct);

            if (runs.Count == 0)
                return sources;

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

                string? why =
                    run.Name != manifest.Name ? "it is not a run of this manifest's train"
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

                if (why is not null)
                {
                    logger.LogInformation(
                        "The retry of manifest {ManifestId} reruns from the top instead of "
                            + "resuming run {FailedRun}: {Reason}",
                        manifest.Id,
                        runId,
                        why
                    );
                    continue;
                }

                candidates.Add((manifest, runId));
            }
        }

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

            var verdict = await resumes.Check(
                declared.Train,
                declared.Chain,
                declared.Input,
                declared.Output,
                runId,
                resumeAt: null,
                ct
            );

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
}
