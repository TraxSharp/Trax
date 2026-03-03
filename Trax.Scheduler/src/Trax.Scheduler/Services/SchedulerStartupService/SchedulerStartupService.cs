using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Scheduler.Services.SchedulerStartupService;

/// <summary>
/// One-shot hosted service that runs startup tasks before the polling services begin.
/// </summary>
/// <remarks>
/// Registered first in DI so that .NET's sequential IHostedService startup order
/// guarantees this completes before ManifestManagerPollingService or
/// JobDispatcherPollingService begin polling.
/// </remarks>
internal class SchedulerStartupService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    ILogger<SchedulerStartupService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.RecoverStuckJobsOnStartup)
            await RecoverStuckJobs(cancellationToken);

        await SeedPendingManifests(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecoverStuckJobs(CancellationToken cancellationToken)
    {
        var serverStartTime = DateTime.UtcNow;

        using var scope = serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var stuckJobs = await dataContext
            .Metadatas.Where(m =>
                m.TrainState == TrainState.InProgress && m.StartTime < serverStartTime
            )
            .ToListAsync(cancellationToken);

        if (stuckJobs.Count == 0)
        {
            logger.LogInformation(
                "RecoverStuckJobs: no in-progress jobs found from before server start"
            );
            return;
        }

        foreach (var metadata in stuckJobs)
        {
            metadata.TrainState = TrainState.Failed;
            metadata.EndTime = DateTime.UtcNow;
            metadata.AddException(
                new InvalidOperationException("Server restarted while job was in progress")
            );
        }

        await dataContext.SaveChanges(cancellationToken);

        logger.LogWarning(
            "RecoverStuckJobs: failed {Count} stuck in-progress job(s) from before server start at {ServerStartTime}",
            stuckJobs.Count,
            serverStartTime
        );
    }

    private async Task SeedPendingManifests(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();

        // Seed manifests from startup configuration
        if (configuration.PendingManifests.Count > 0)
        {
            logger.LogInformation(
                "Seeding {Count} pending manifest(s) from startup configuration...",
                configuration.PendingManifests.Count
            );

            var scheduler = scope.ServiceProvider.GetRequiredService<ITraxScheduler>();

            foreach (var pending in configuration.PendingManifests)
            {
                try
                {
                    await pending.ScheduleFunc(scheduler, cancellationToken);
                    logger.LogDebug("Seeded manifest: {ExternalId}", pending.ExternalId);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to seed manifest {ExternalId}: {Message}",
                        pending.ExternalId,
                        ex.Message
                    );
                    throw;
                }
            }

            logger.LogInformation(
                "Successfully seeded {Count} manifest(s)",
                configuration.PendingManifests.Count
            );
        }

        // Prune orphaned manifests (manifests in DB that are no longer in the startup configuration)
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        if (configuration.PruneOrphanedManifests)
        {
            var expectedExternalIds = configuration
                .PendingManifests.SelectMany(p => p.ExpectedExternalIds)
                .ToHashSet();

            await PruneOrphanedManifestsAsync(dataContext, expectedExternalIds, cancellationToken);
        }

        // Clean up orphaned ManifestGroups (groups with no manifests remaining)
        var orphanedCount = await dataContext
            .ManifestGroups.Where(g => !g.Manifests.Any())
            .ExecuteDeleteAsync(cancellationToken);

        if (orphanedCount > 0)
            logger.LogInformation("Cleaned up {Count} orphaned manifest group(s)", orphanedCount);

        // Release closures and captured batch lists that are no longer needed
        configuration.PendingManifests.Clear();
    }

    private async Task PruneOrphanedManifestsAsync(
        IDataContext dataContext,
        HashSet<string> expectedExternalIds,
        CancellationToken cancellationToken
    )
    {
        // Find all manifest IDs whose ExternalId is not in the configured set
        var orphanedManifestIds = await dataContext
            .Manifests.Where(m => !expectedExternalIds.Contains(m.ExternalId))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (orphanedManifestIds.Count == 0)
        {
            logger.LogDebug("No orphaned manifests found");
            return;
        }

        // Clear self-referencing FK (DependsOnManifestId) for any manifest pointing to an orphan.
        // This handles both orphan→orphan and kept→orphan references.
        await dataContext
            .Manifests.Where(m =>
                m.DependsOnManifestId.HasValue
                && orphanedManifestIds.Contains(m.DependsOnManifestId.Value)
            )
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.DependsOnManifestId, (long?)null),
                cancellationToken
            );

        // Delete in FK-dependency order: WorkQueues → DeadLetters → Metadata → Manifests
        await dataContext
            .WorkQueues.Where(w =>
                w.ManifestId.HasValue && orphanedManifestIds.Contains(w.ManifestId.Value)
            )
            .ExecuteDeleteAsync(cancellationToken);

        await dataContext
            .DeadLetters.Where(d => orphanedManifestIds.Contains(d.ManifestId))
            .ExecuteDeleteAsync(cancellationToken);

        await dataContext
            .Metadatas.Where(m =>
                m.ManifestId.HasValue && orphanedManifestIds.Contains(m.ManifestId.Value)
            )
            .ExecuteDeleteAsync(cancellationToken);

        var pruned = await dataContext
            .Manifests.Where(m => orphanedManifestIds.Contains(m.Id))
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Pruned {Count} orphaned manifest(s) from the database", pruned);
    }
}
