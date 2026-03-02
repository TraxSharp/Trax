using Trax.Effect.Models.WorkQueue;

namespace Trax.Scheduler.Trains.JobDispatcher.Steps;

/// <summary>
/// Carries dispatch capacity information between steps in the JobDispatcher train.
/// </summary>
internal record DispatchContext(
    List<WorkQueue> Entries,
    int ActiveMetadataCount,
    Dictionary<long, int> GroupActiveCounts,
    Dictionary<long, int> GroupLimits
);
