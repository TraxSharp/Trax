using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Enables automatic cleanup of metadata for system and other noisy trains.
    /// </summary>
    /// <remarks>
    /// By default, metadata from <c>ManifestManagerTrain</c> and
    /// <c>MetadataCleanupTrain</c> will be cleaned up. Additional train types
    /// can be added via the configure action.
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
