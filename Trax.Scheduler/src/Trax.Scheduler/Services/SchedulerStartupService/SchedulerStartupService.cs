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
        // RecoverStuckJobs only makes sense with a real database — in-memory data is
        // lost on restart, so there are no stuck jobs to recover.
        if (configuration.RecoverStuckJobsOnStartup && configuration.HasDatabaseProvider)
            await RecoverStuckJobs(cancellationToken);

        await SeedPendingManifests(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecoverStuckJobs(CancellationToken cancellationToken)
    {
        var serverStartTime = DateTime.UtcNow;

        using var scope = serviceProvider.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var totalRecovered = 0;

        while (true)
        {
            var stuckIds = await dataContext
                .Metadatas.Where(m =>
                    m.TrainState == TrainState.InProgress && m.StartTime < serverStartTime
                )
                .OrderBy(m => m.Id)
                .Select(m => m.Id)
                .Take(PruneBatchSize)
                .ToListAsync(cancellationToken);

            if (stuckIds.Count == 0)
                break;

            var now = DateTime.UtcNow;

            await dataContext
                .Metadatas.Where(m =>
                    stuckIds.Contains(m.Id) && m.TrainState == TrainState.InProgress
                )
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(m => m.TrainState, TrainState.Failed)
                            .SetProperty(m => m.EndTime, now)
                            .SetProperty(
                                m => m.FailureReason,
                                "Server restarted while job was in progress"
                            )
                            .SetProperty(m => m.FailureException, "ServerRestart")
                            .SetProperty(m => m.FailureJunction, nameof(SchedulerStartupService)),
                    cancellationToken
                );

            totalRecovered += stuckIds.Count;
        }

        if (totalRecovered > 0)
            logger.LogWarning(
                "RecoverStuckJobs: failed {Count} stuck in-progress job(s) from before server start at {ServerStartTime}",
                totalRecovered,
                serverStartTime
            );
        else
            logger.LogInformation(
                "RecoverStuckJobs: no in-progress jobs found from before server start"
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
                await SeedWithRetryAsync(
                    async ct => await pending.ScheduleFunc(scheduler, ct),
                    pending.ExternalId,
                    cancellationToken
                );
            }

            logger.LogInformation(
                "Successfully seeded {Count} manifest(s)",
                configuration.PendingManifests.Count
            );
        }

        // Prune and cleanup use ExecuteDeleteAsync/ExecuteUpdateAsync which are not
        // supported by the InMemory EF Core provider. They're also unnecessary with
        // InMemory since the database starts empty on each restart.
        if (configuration.HasDatabaseProvider)
        {
            var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

            if (configuration.PruneOrphanedManifests)
            {
                var expectedExternalIds = configuration
                    .PendingManifests.SelectMany(p => p.ExpectedExternalIds)
                    .ToHashSet();

                await PruneOrphanedManifestsAsync(
                    dataContext,
                    expectedExternalIds,
                    cancellationToken
                );
            }

            // Clean up orphaned ManifestGroups (groups with no manifests remaining)
            var orphanedCount = await dataContext
                .ManifestGroups.Where(g => !g.Manifests.Any())
                .ExecuteDeleteAsync(cancellationToken);

            if (orphanedCount > 0)
                logger.LogInformation(
                    "Cleaned up {Count} orphaned manifest group(s)",
                    orphanedCount
                );
        }

        // Release closures and captured batch lists that are no longer needed
        configuration.PendingManifests.Clear();
    }

    internal const int DefaultMaxRetries = 5;
    internal static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(2);

    internal async Task SeedWithRetryAsync(
        Func<CancellationToken, Task> action,
        string externalId,
        CancellationToken cancellationToken,
        int maxRetries = DefaultMaxRetries,
        TimeSpan? baseDelay = null
    )
    {
        var delay = baseDelay ?? DefaultBaseDelay;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await action(cancellationToken);
                logger.LogDebug("Seeded manifest: {ExternalId}", externalId);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                var retryDelay = delay * Math.Pow(2, attempt - 1);
                logger.LogWarning(
                    ex,
                    "Transient failure seeding manifest {ExternalId} (attempt {Attempt}/{MaxRetries}), retrying in {Delay}s",
                    externalId,
                    attempt,
                    maxRetries,
                    retryDelay.TotalSeconds
                );
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to seed manifest {ExternalId}: {Message}",
                    externalId,
                    ex.Message
                );
                throw;
            }
        }
    }

    internal static bool IsTransient(Exception ex) =>
        ex is TimeoutException
        || ex.GetType().FullName?.StartsWith("Npgsql.") == true
        || (
            ex is InvalidOperationException
            && ex.InnerException is not null
            && IsTransient(ex.InnerException)
        );

    /// <summary>
    /// Maximum number of orphaned manifests to delete per batch. Keeps the SQL IN(...)
    /// clause small enough to avoid command timeouts on large prune operations.
    /// </summary>
    internal const int PruneBatchSize = 500;

    private async Task PruneOrphanedManifestsAsync(
        IDataContext dataContext,
        HashSet<string> expectedExternalIds,
        CancellationToken cancellationToken
    )
    {
        // --- Server compute: load lightweight ID pairs, compute orphan set in C# ---
        //
        // Why not filter in the database?
        // EF Core translates HashSet.Contains() into a SQL NOT IN(...) with every element
        // as a literal parameter. With 5000+ expected IDs, this generates a massive SQL
        // statement that can exceed Postgres's command timeout just in query planning on
        // low-resource instances (2 vCPUs).
        //
        // Instead, we fetch all (id, external_id) pairs — a lightweight projection that
        // transfers ~300KB even at 10K manifests — and compute the set difference in C#
        // where it's a trivial O(n) HashSet lookup. The database only sees simple queries
        // with small IN(...) clauses during the batched deletes.
        var allManifests = await dataContext
            .Manifests.Select(m => new { m.Id, m.ExternalId })
            .ToListAsync(cancellationToken);

        var orphanedManifestIds = allManifests
            .Where(m => !expectedExternalIds.Contains(m.ExternalId))
            .Select(m => m.Id)
            .ToList();

        if (orphanedManifestIds.Count == 0)
        {
            logger.LogDebug("No orphaned manifests found");
            return;
        }

        logger.LogInformation(
            "Found {OrphanCount} orphaned manifest(s) to prune (of {TotalCount} total)",
            orphanedManifestIds.Count,
            allManifests.Count
        );

        // --- Database compute: delete orphans in batches by integer PK ---
        //
        // Each batch generates WHERE id IN (1, 2, ..., 500) — 500 integer PKs is a
        // trivial query plan for Postgres regardless of instance size.
        var totalPruned = 0;

        foreach (var batch in orphanedManifestIds.Chunk(PruneBatchSize))
        {
            var batchIds = batch.ToList();

            // Clear self-referencing FK (DependsOnManifestId) for any manifest pointing to
            // an orphan in this batch. Handles both orphan→orphan and kept→orphan references.
            await dataContext
                .Manifests.Where(m =>
                    m.DependsOnManifestId.HasValue && batchIds.Contains(m.DependsOnManifestId.Value)
                )
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.DependsOnManifestId, (long?)null),
                    cancellationToken
                );

            // Delete in FK-dependency order: WorkQueues → DeadLetters → Metadata → Manifests
            await dataContext
                .WorkQueues.Where(w =>
                    w.ManifestId.HasValue && batchIds.Contains(w.ManifestId.Value)
                )
                .ExecuteDeleteAsync(cancellationToken);

            await dataContext
                .DeadLetters.Where(d => batchIds.Contains(d.ManifestId))
                .ExecuteDeleteAsync(cancellationToken);

            await dataContext
                .Metadatas.Where(m =>
                    m.ManifestId.HasValue && batchIds.Contains(m.ManifestId.Value)
                )
                .ExecuteDeleteAsync(cancellationToken);

            var pruned = await dataContext
                .Manifests.Where(m => batchIds.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken);

            totalPruned += pruned;

            logger.LogInformation(
                "Pruned batch of {BatchCount} orphaned manifest(s) ({TotalCount} total so far)",
                pruned,
                totalPruned
            );
        }

        logger.LogInformation(
            "Finished pruning {Count} orphaned manifest(s) from the database",
            totalPruned
        );
    }
}
