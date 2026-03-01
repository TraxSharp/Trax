using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Workflows.MetadataCleanup;

namespace Trax.Scheduler.Services.MetadataCleanupPollingService;

/// <summary>
/// Background service that periodically runs the metadata cleanup workflow.
/// </summary>
/// <remarks>
/// This service polls on the configured <see cref="MetadataCleanupConfiguration.CleanupInterval"/>
/// and delegates the actual cleanup logic to <see cref="IMetadataCleanupWorkflow"/>.
/// Errors are logged and swallowed to keep the polling loop running.
/// </remarks>
internal class MetadataCleanupPollingService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    ILogger<MetadataCleanupPollingService> logger
) : BackgroundService
{
    /// <summary>
    /// Runs the metadata cleanup polling loop on the configured interval.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cleanupConfig = configuration.MetadataCleanup!;

        logger.LogInformation(
            "MetadataCleanupPollingService starting with interval {Interval}, retention {Retention}, whitelist [{Whitelist}]",
            cleanupConfig.CleanupInterval,
            cleanupConfig.RetentionPeriod,
            string.Join(", ", cleanupConfig.WorkflowTypeWhitelist)
        );

        using var timer = new PeriodicTimer(cleanupConfig.CleanupInterval);

        // Run immediately on startup before waiting for the first tick
        await RunCleanup(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCleanup(stoppingToken);
        }

        logger.LogInformation("MetadataCleanupPollingService stopping");
    }

    private async Task RunCleanup(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var workflow = scope.ServiceProvider.GetRequiredService<IMetadataCleanupWorkflow>();

            logger.LogDebug("Metadata cleanup cycle starting");
            await workflow.Run(new MetadataCleanupRequest(), cancellationToken);
            logger.LogDebug("Metadata cleanup cycle completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during metadata cleanup cycle");
        }
    }
}
