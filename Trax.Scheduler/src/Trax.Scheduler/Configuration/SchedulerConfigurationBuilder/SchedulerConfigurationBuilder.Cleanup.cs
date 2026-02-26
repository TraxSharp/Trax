using Trax.Scheduler.Workflows.ManifestManager;
using Trax.Scheduler.Workflows.MetadataCleanup;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Enables automatic cleanup of metadata for system and other noisy workflows.
    /// </summary>
    /// <remarks>
    /// By default, metadata from <c>ManifestManagerWorkflow</c> and
    /// <c>MetadataCleanupWorkflow</c> will be cleaned up. Additional workflow types
    /// can be added via the configure action.
    ///
    /// <code>
    /// .AddScheduler(scheduler => scheduler
    ///     .AddMetadataCleanup(cleanup =>
    ///     {
    ///         cleanup.RetentionPeriod = TimeSpan.FromHours(2);
    ///         cleanup.CleanupInterval = TimeSpan.FromMinutes(1);
    ///         cleanup.AddWorkflowType&lt;MyNoisyWorkflow&gt;();
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

        // Add default workflow types whose metadata should be cleaned up
        config.AddWorkflowType<ManifestManagerWorkflow>();
        config.AddWorkflowType<MetadataCleanupWorkflow>();

        configure?.Invoke(config);

        _configuration.MetadataCleanup = config;

        return this;
    }
}
