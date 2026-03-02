using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;
using Trax.Scheduler.Trains.TaskServerExecutor;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Central registry of internal/administrative scheduler trains.
/// These trains are excluded from dashboard statistics and max-active-job counts.
/// </summary>
public static class AdminTrains
{
    /// <summary>
    /// The train types considered administrative/internal to the scheduler.
    /// </summary>
    public static readonly IReadOnlyList<Type> Types =
    [
        typeof(IManifestManagerTrain),
        typeof(ManifestManagerTrain),
        typeof(ITaskServerExecutorTrain),
        typeof(TaskServerExecutorTrain),
        typeof(IMetadataCleanupTrain),
        typeof(MetadataCleanupTrain),
        typeof(IJobDispatcherTrain),
        typeof(JobDispatcherTrain),
    ];

    /// <summary>
    /// Fully qualified type names of admin trains.
    /// </summary>
    public static readonly IReadOnlyList<string> FullNames = Types
        .Select(t => t.FullName!)
        .ToList();

    /// <summary>
    /// Short (unqualified) type names of admin trains, used for display filtering.
    /// </summary>
    public static readonly IReadOnlyList<string> ShortNames = Types.Select(t => t.Name).ToList();
}
