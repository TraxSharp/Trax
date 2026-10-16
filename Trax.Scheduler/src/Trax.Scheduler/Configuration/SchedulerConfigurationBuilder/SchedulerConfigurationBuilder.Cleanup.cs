using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Configures the automatic cleanup of metadata for the scheduler's own trains and other noisy
    /// trains.
    /// </summary>
    /// <remarks>
    /// A scheduler with a database provider cleans up its internal trains (JobDispatcher,
    /// ManifestManager, MetadataCleanup, DeadLetterCleanup, JobRunner) with the default settings
    /// whether or not this is called: the dispatcher alone persists a metadata row every poll, so
    /// leaving any of them out lets <c>trax.metadata</c> grow without bound. Call this to change
    /// the interval or retention, or to add your own noisy trains on top.
    ///
    /// <code>
    /// .AddScheduler(scheduler => scheduler
    ///     .AddMetadataCleanup(cleanup =>
    ///     {
    ///         cleanup.RetentionPeriod = TimeSpan.FromHours(2);
    ///         cleanup.CleanupInterval = TimeSpan.FromMinutes(1);
    ///         cleanup.AddTrainType&lt;MyNoisyTrain&gt;();
    ///     })
    /// )
    /// </code>
    /// </remarks>
    /// <param name="configure">Optional action to customize cleanup behavior</param>
    /// <returns>The builder for method chaining</returns>
    public SchedulerConfigurationBuilder AddMetadataCleanup(
        Action<MetadataCleanupConfiguration>? configure = null
    )
    {
        var config = new MetadataCleanupConfiguration();

        // Add default train types whose metadata should be cleaned up
        config.AddTrainType<ManifestManagerTrain>();
        config.AddTrainType<MetadataCleanupTrain>();

        configure?.Invoke(config);

        _configuration.MetadataCleanup = config;

        return this;
    }
}
