using Trax.Effect.Models.WorkQueue;

namespace Trax.Scheduler.Workflows.JobDispatcher.Steps;

/// <summary>
/// Carries dispatch capacity information between steps in the JobDispatcher workflow.
/// </summary>
internal record DispatchContext(
    List<WorkQueue> Entries,
    int ActiveMetadataCount,
    Dictionary<long, int> GroupActiveCounts,
    Dictionary<long, int> GroupLimits
);
