using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

/// <summary>
/// An InProgress run as the timeout canceller and the stale reaper read it.
/// </summary>
internal sealed record TimedRun(
    long Id,
    long? ParentId,
    string Name,
    DateTime StartTime,
    long? ManifestId,
    int? ManifestTimeoutSeconds
);

/// <summary>
/// What bounds a run: its effective timeout (<see langword="null"/> when nothing bounds it) and
/// whether it, or the run it is nested in, is one a scheduler must leave alone.
/// </summary>
internal sealed record RunBound(TimeSpan? Timeout, bool Excluded, long? RootManifestId);

/// <summary>
/// Resolves a run's timeout from the run at the root of its <c>ParentId</c> chain, so a train
/// nested inside a scheduled run is bounded by the scheduled run's manifest and not by the
/// global default.
/// </summary>
/// <remarks>
/// The root's manifest <c>TimeoutSeconds</c> applies when it has one. Otherwise
/// <see cref="SchedulerConfiguration.DefaultJobTimeout"/> applies only when a scheduler dispatched
/// the root: it has a manifest, a work queue entry names it, or a <c>background_job</c> row runs it.
/// A run started directly on the train bus by a host sharing the database has none of those and
/// is not bounded here; the stale in-progress reaper remains its safety net. A run is excluded
/// when its own name or its root's is in
/// <see cref="SchedulerConfiguration.ExcludedTrainTypeNames"/> or <see cref="AdminTrains"/>.
///
/// The walk up the chain is bounded by <see cref="MaxDepth"/> queries, each loading one level of
/// ancestors for the whole batch. A chain deeper than that, one that loops, or one whose parent
/// row is missing has no resolved root and is left unbounded, never cancelled on a guess.
/// </remarks>
internal static class RunTimeouts
{
    /// <summary>
    /// How many levels of nesting are followed to find a run's root.
    /// </summary>
    internal const int MaxDepth = 16;

    internal static async Task<Dictionary<long, RunBound>> ResolveAsync(
        IDataContext dataContext,
        SchedulerConfiguration config,
        IReadOnlyCollection<TimedRun> runs,
        CancellationToken cancellationToken
    )
    {
        var known = runs.ToDictionary(r => r.Id);

        var frontier = MissingParents(runs, known);
        for (var level = 0; level < MaxDepth && frontier.Count > 0; level++)
        {
            var ancestors = await dataContext
                .Metadatas.Where(m => frontier.Contains(m.Id))
                .Select(m => new TimedRun(
                    m.Id,
                    m.ParentId,
                    m.Name,
                    m.StartTime,
                    m.ManifestId,
                    m.Manifest != null ? m.Manifest.TimeoutSeconds : null
                ))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var ancestor in ancestors)
                known.TryAdd(ancestor.Id, ancestor);

            frontier = MissingParents(ancestors, known);
        }

        var roots = new Dictionary<long, TimedRun?>();
        foreach (var run in runs)
            roots[run.Id] = FindRoot(run, known);

        var unmanifestedRootIds = roots
            .Values.Where(r => r is { ManifestId: null })
            .Select(r => r!.Id)
            .Distinct()
            .ToList();

        var dispatchedRootIds = new HashSet<long>();
        if (unmanifestedRootIds.Count > 0)
        {
            dispatchedRootIds.UnionWith(
                await dataContext
                    .WorkQueues.Where(q =>
                        q.MetadataId != null && unmanifestedRootIds.Contains(q.MetadataId.Value)
                    )
                    .Select(q => q.MetadataId!.Value)
                    .ToListAsync(cancellationToken)
            );
            dispatchedRootIds.UnionWith(
                await dataContext
                    .BackgroundJobs.Where(j => unmanifestedRootIds.Contains(j.MetadataId))
                    .Select(j => j.MetadataId)
                    .ToListAsync(cancellationToken)
            );
        }

        var result = new Dictionary<long, RunBound>(runs.Count);
        foreach (var run in runs)
        {
            var root = roots[run.Id];
            var excluded =
                IsExcluded(config, run.Name) || (root is not null && IsExcluded(config, root.Name));

            TimeSpan? timeout = root switch
            {
                null => null,
                { ManifestTimeoutSeconds: { } seconds } => TimeSpan.FromSeconds(seconds),
                { ManifestId: not null } => config.DefaultJobTimeout,
                _ when dispatchedRootIds.Contains(root.Id) => config.DefaultJobTimeout,
                _ => null,
            };

            result[run.Id] = new RunBound(timeout, excluded, root?.ManifestId);
        }

        return result;
    }

    private static List<long> MissingParents(
        IEnumerable<TimedRun> runs,
        Dictionary<long, TimedRun> known
    ) =>
        runs.Where(r => r.ParentId is { } p && !known.ContainsKey(p))
            .Select(r => r.ParentId!.Value)
            .Distinct()
            .ToList();

    private static TimedRun? FindRoot(TimedRun run, Dictionary<long, TimedRun> known)
    {
        var current = run;
        for (var step = 0; step <= MaxDepth; step++)
        {
            if (current.ParentId is not { } parentId)
                return current;

            // A parent past the depth bound, or deleted between the reads: no root is resolved.
            if (!known.TryGetValue(parentId, out var parent))
                return null;

            current = parent;
        }

        return null;
    }

    private static bool IsExcluded(SchedulerConfiguration config, string name) =>
        config.ExcludedTrainTypeNames.Contains(name) || AdminTrains.FullNames.Contains(name);
}
