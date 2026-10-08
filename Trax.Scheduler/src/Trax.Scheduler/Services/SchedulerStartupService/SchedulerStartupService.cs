using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.ManifestPruning;
using Trax.Scheduler.Services.RunOutcomes;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Scheduler.Services.SchedulerStartupService;

/// <summary>
/// One-shot hosted service that runs startup tasks before the polling services begin.
/// </summary>
/// <remarks>
/// With <see cref="SchedulerConfiguration.RecoverStuckJobsOnStartup"/> on (the default) and a
/// database provider registered, it first fails every <c>InProgress</c> run in the shared
/// database whose <c>StartTime</c> precedes this host's start, regardless of which host or worker
/// is running it.
///
/// <para>
/// Registered first in DI so that .NET's sequential IHostedService startup order
/// guarantees this completes before ManifestManagerPollingService or
/// JobDispatcherPollingService begin polling.
/// </para>
/// </remarks>
internal class SchedulerStartupService(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration,
    ILogger<SchedulerStartupService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        configuration.Owner ??= ResolveOwner(serviceProvider);

        if (configuration.UnreachableRetriesWarning() is { } warning)
            logger.LogWarning("{Warning}", warning);

        // RecoverStuckJobs only makes sense with a real database — in-memory data is
        // lost on restart, so there are no stuck jobs to recover.
        if (configuration.RecoverStuckJobsOnStartup && configuration.HasDatabaseProvider)
            await RecoverStuckJobs(cancellationToken);

        await SeedPendingManifests(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The name this application's manifests carry as their owner: the host environment's
    /// <c>ApplicationName</c>, or the entry assembly's name when no host environment is
    /// registered. Null when neither gives a name, in which case the startup prune deletes nothing.
    /// </summary>
    internal static string? ResolveOwner(IServiceProvider services)
    {
        var name =
            services.GetService<IHostEnvironment>()?.ApplicationName
            ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Fails every <c>InProgress</c> run in the shared database whose <c>StartTime</c> precedes
    /// this host's start, regardless of which host or worker is running it.
    /// </summary>
    /// <remarks>
    /// Nothing records which process owns a run, so "started before this host" is the whole
    /// test. When one process runs everything, that is exactly the runs a crash or restart
    /// orphaned. Where several hosts or remote workers share the database, a run still executing
    /// on one of them is failed too. The recovery itself requeues nothing. Each run it fails is
    /// published to the lifecycle hooks as <c>Failed</c>, once.
    /// </remarks>
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

            // One conditional write per run, so only the runs this recovery failed are published:
            // a run that finished meanwhile matches nothing.
            var recovered = new List<long>(stuckIds.Count);

            foreach (var id in stuckIds)
            {
                var failed = await dataContext
                    .Metadatas.Where(m => m.Id == id && m.TrainState == TrainState.InProgress)
                    .ExecuteUpdateAsync(
                        s =>
                            s.SetProperty(m => m.TrainState, TrainState.Failed)
                                .SetProperty(m => m.EndTime, now)
                                .SetProperty(
                                    m => m.FailureReason,
                                    "Server restarted while job was in progress"
                                )
                                .SetProperty(m => m.FailureException, "ServerRestart")
                                .SetProperty(
                                    m => m.FailureJunction,
                                    nameof(SchedulerStartupService)
                                ),
                        cancellationToken
                    );

                if (failed > 0)
                    recovered.Add(id);
            }

            // Committed: no transaction is open. No train is left to publish these outcomes.
            await OutOfTrainOutcomes.PublishFailedAsync(serviceProvider, recovered, logger);

            totalRecovered += recovered.Count;
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

                // Pruning is housekeeping: a failure is logged and the host still starts.
                try
                {
                    await PruneOrphanedManifestsAsync(
                        dataContext,
                        expectedExternalIds,
                        cancellationToken
                    );
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "Pruning orphaned manifests failed; the host starts anyway and the prune runs again at the next start"
                    );
                }
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

    /// <summary>
    /// Whether a seeding failure is worth retrying, as the provider's SQL dialect classifies it:
    /// a lost or refused connection, a timeout, a deadlock or serialization failure, or (Sqlite) a
    /// busy or locked database. Any other failure, a constraint violation included, is thrown at
    /// once. A host with no dialect (InMemory) retries nothing, since it has no database to wait
    /// for.
    /// </summary>
    internal bool IsTransient(Exception ex) =>
        serviceProvider.GetService<ISqlDialect>()?.IsTransient(ex) ?? false;

    /// <summary>
    /// Maximum number of orphaned manifests to delete per batch. Keeps the SQL IN(...)
    /// clause small enough to avoid command timeouts on large prune operations.
    /// </summary>
    internal const int PruneBatchSize = ManifestPruner.BatchSize;

    /// <summary>
    /// Deletes this application's manifests that its configuration no longer declares.
    /// </summary>
    /// <remarks>
    /// A host that declares no manifests prunes nothing: an API or worker host that calls
    /// <c>AddScheduler</c> only to reach the scheduler services has no basis for calling another
    /// host's manifests orphaned. Only manifests whose <c>Owner</c> is this application's name are
    /// candidates, so a manifest another application declares against the same database, or one
    /// with no owner at all (written before owners were recorded and not declared since), is never
    /// deleted here; when this application's name cannot be found, nothing is. A manifest with a
    /// pending or running run is kept until the run finishes (see <see cref="ManifestPruner"/>).
    /// </remarks>
    private async Task PruneOrphanedManifestsAsync(
        IDataContext dataContext,
        HashSet<string> expectedExternalIds,
        CancellationToken cancellationToken
    )
    {
        if (expectedExternalIds.Count == 0)
        {
            logger.LogInformation(
                "Skipping orphaned manifest pruning: this host declares no manifests, so it has no basis to call any manifest orphaned"
            );
            return;
        }

        if (configuration.Owner is not { } owner)
        {
            logger.LogWarning(
                "Skipping orphaned manifest pruning: this host has no application name, so it cannot tell its own manifests from another application's"
            );
            return;
        }

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
            .Manifests.Where(m => m.Owner == owner)
            .Select(m => new { m.Id, m.ExternalId })
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
            "Found {OrphanCount} orphaned manifest(s) to prune (of {TotalCount} owned by {Owner})",
            orphanedManifestIds.Count,
            allManifests.Count,
            owner
        );

        var (pruned, kept) = await ManifestPruner.PruneAsync(
            dataContext,
            orphanedManifestIds,
            logger,
            cancellationToken
        );

        logger.LogInformation(
            "Finished pruning {Count} orphaned manifest(s) from the database ({Kept} kept until their runs finish)",
            pruned,
            kept
        );
    }
}
