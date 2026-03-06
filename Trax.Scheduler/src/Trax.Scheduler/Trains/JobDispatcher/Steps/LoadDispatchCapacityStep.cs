using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.EffectStep;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Trains.JobDispatcher.Steps;

/// <summary>
/// Loads dispatch capacity: global active count, per-group active counts, and per-group limits.
/// Short-circuits with empty entries when there is nothing to dispatch or the global limit is reached.
/// </summary>
internal class LoadDispatchCapacityStep(
    IDataContext dataContext,
    SchedulerConfiguration config,
    ILogger<LoadDispatchCapacityStep> logger,
    ITrainDiscoveryService? discoveryService = null
) : EffectStep<List<WorkQueue>, DispatchContext>
{
    private static readonly DispatchContext Empty = new([], 0, [], []);

    public override async Task<DispatchContext> Run(List<WorkQueue> entries)
    {
        if (entries.Count == 0)
            return Empty;

        var excluded = TrainNameExpander
            .ExpandTrainNames(config.ExcludedTrainTypeNames, discoveryService)
            .ToList();

        // Single query: left-join Metadatas → Manifests, grouped by ManifestGroupId.
        // Rows without a manifest group under null. This gives both the global active
        // count (sum of all groups) and per-group active counts in one round-trip.
        var activeCounts = await dataContext
            .Metadatas.Where(m =>
                !excluded.Contains(m.Name)
                && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
            )
            .GroupJoin(
                dataContext.Manifests,
                m => m.ManifestId,
                man => man.Id,
                (m, manifests) => new { m, manifests }
            )
            .SelectMany(
                x => x.manifests.DefaultIfEmpty(),
                (x, man) => new { GroupId = man == null ? (long?)null : (long?)man.ManifestGroupId }
            )
            .GroupBy(x => x.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToListAsync(CancellationToken);

        var activeMetadataCount = activeCounts.Sum(x => x.Count);

        // Check global capacity — return empty entries to short-circuit dispatch
        if (config.MaxActiveJobs.HasValue && activeMetadataCount >= config.MaxActiveJobs.Value)
        {
            logger.LogDebug(
                "MaxActiveJobs limit reached ({ActiveCount}/{MaxActiveJobs}). Skipping dispatch this cycle.",
                activeMetadataCount,
                config.MaxActiveJobs.Value
            );
            return new DispatchContext([], activeMetadataCount, [], []);
        }

        var groupActiveCounts = activeCounts
            .Where(x => x.GroupId.HasValue)
            .ToDictionary(x => x.GroupId!.Value, x => x.Count);

        // Load per-group limits (only groups that have a limit set)
        var groupLimits = await dataContext
            .ManifestGroups.Where(g => g.MaxActiveJobs != null)
            .ToDictionaryAsync(g => g.Id, g => g.MaxActiveJobs!.Value, CancellationToken);

        return new DispatchContext(entries, activeMetadataCount, groupActiveCounts, groupLimits);
    }
}
