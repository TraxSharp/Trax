using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Services.ManifestManagerPollingService;

/// <summary>
/// Background service that polls for due manifests on a configurable interval
/// and runs <see cref="IManifestManagerTrain"/> each cycle.
/// </summary>
/// <remarks>
/// With PostgreSQL, uses an advisory lock (<c>pg_try_advisory_xact_lock</c>) to ensure
/// only one server instance runs the manifest evaluation cycle at a time,
/// preventing duplicate WorkQueue entries in multi-server deployments.
///
/// With InMemory, runs the train directly without transactions or advisory locks.
/// The resolved <see cref="IManifestManagerTrain"/> is <see cref="InMemoryManifestManagerTrain"/>
/// which dispatches jobs inline via <see cref="Services.JobSubmitter.InMemoryJobSubmitter"/>.
/// </remarks>
internal class ManifestManagerPollingService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    ILogger<ManifestManagerPollingService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "ManifestManagerPollingService starting with polling interval {Interval}",
            configuration.ManifestManagerPollingInterval
        );

        using var timer = new PeriodicTimer(configuration.ManifestManagerPollingInterval);

        await RunManifestManager(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunManifestManager(stoppingToken);
        }

        logger.LogInformation("ManifestManagerPollingService stopping");
    }

    private async Task RunManifestManager(CancellationToken cancellationToken)
    {
        if (!configuration.ManifestManagerEnabled)
        {
            logger.LogDebug("ManifestManager is disabled, skipping polling cycle");
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();

            // Advisory lock: single-leader election for manifest evaluation.
            // Prevents duplicate WorkQueue entries when multiple servers poll simultaneously.
            // InMemory doesn't support transactions or advisory locks — run without lock.
            if (configuration.HasDatabaseProvider)
            {
                var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

                using var transaction = await dataContext.BeginTransaction(cancellationToken);

                var acquired = await ((DbContext)dataContext)
                    .Database.SqlQuery<bool>(
                        $"""SELECT pg_try_advisory_xact_lock(hashtext('trax_manifest_manager')) AS "Value" """
                    )
                    .FirstAsync(cancellationToken);

                if (!acquired)
                {
                    logger.LogDebug("Another server is running ManifestManager, skipping cycle");
                    await dataContext.RollbackTransaction();
                    return;
                }

                // Run train within the advisory lock transaction
                var train = scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();

                logger.LogDebug("ManifestManager polling cycle starting");
                await train.Run(Unit.Default, cancellationToken);
                logger.LogDebug("ManifestManager polling cycle completed");

                await dataContext.CommitTransaction();
            }
            else
            {
                var train = scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();

                logger.LogDebug("ManifestManager polling cycle starting");
                await train.Run(Unit.Default, cancellationToken);
                logger.LogDebug("ManifestManager polling cycle completed");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during ManifestManager polling cycle");
        }
    }
}
