using Microsoft.Extensions.Logging;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.EffectStep;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Workflows.JobDispatcher.Steps;

/// <summary>
/// Filters queued entries against global and per-group capacity limits.
/// </summary>
/// <remarks>
/// Per-group limits prevent starvation: when a high-priority group hits its cap, lower-priority
/// groups can still dispatch (using <c>continue</c> instead of <c>break</c>).
/// </remarks>
internal class ApplyCapacityLimitsStep(
    SchedulerConfiguration config,
    ILogger<ApplyCapacityLimitsStep> logger
) : EffectStep<DispatchContext, List<WorkQueue>>
{
    public override Task<List<WorkQueue>> Run(DispatchContext context)
    {
        var toDispatch = new List<WorkQueue>();
        var groupDispatchCounts = new Dictionary<long, int>();
        var globalDispatched = 0;

        foreach (var entry in context.Entries) // already sorted by group priority
        {
            // Global limit check
            if (
                config.MaxActiveJobs.HasValue
                && context.ActiveMetadataCount + globalDispatched >= config.MaxActiveJobs.Value
            )
                break;

            // Per-group limit check (manual jobs without a manifest skip group limits)
            if (entry.Manifest is not null)
            {
                var groupId = entry.Manifest.ManifestGroupId;
                if (context.GroupLimits.TryGetValue(groupId, out var groupLimit))
                {
                    var active = context.GroupActiveCounts.GetValueOrDefault(groupId, 0);
                    var dispatched = groupDispatchCounts.GetValueOrDefault(groupId, 0);
                    if (active + dispatched >= groupLimit)
                        continue; // skip this group but keep processing other groups
                }

                groupDispatchCounts[groupId] =
                    groupDispatchCounts.GetValueOrDefault(groupId, 0) + 1;
            }

            toDispatch.Add(entry);
            globalDispatched++;
        }

        logger.LogDebug(
            "Dispatch selection: {ToDispatch} of {Total} entries (global active: {ActiveCount})",
            toDispatch.Count,
            context.Entries.Count,
            context.ActiveMetadataCount
        );

        return Task.FromResult(toDispatch);
    }
}
