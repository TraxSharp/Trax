using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Reads the persisted <c>trax.scheduler_config</c> row at startup and applies it to
/// the in-memory <see cref="SchedulerConfiguration"/> singleton (and
/// <see cref="LocalWorkerOptions"/> / <c>MetadataCleanup</c> when registered) so
/// settings survive restarts.
/// </summary>
/// <remarks>
/// Failures are logged but never crash startup. If the table doesn't exist (e.g. an
/// older deployment skipped the migration), or no row is present, the in-memory
/// builder defaults remain in effect.
/// </remarks>
public class SchedulerConfigBootstrapHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SchedulerConfigBootstrapHostedService> _logger;

    public SchedulerConfigBootstrapHostedService(
        IServiceProvider services,
        ILogger<SchedulerConfigBootstrapHostedService> logger
    )
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
            var cfg = scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
            var workerOpts = scope.ServiceProvider.GetService<LocalWorkerOptions>();

            using var db = await factory.CreateDbContextAsync(cancellationToken);
            var row =
                await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    db.SchedulerConfigs,
                    r => r.Id == Effect.Models.SchedulerConfig.SchedulerConfig.SingletonId,
                    cancellationToken
                );

            if (row is null)
            {
                _logger.LogInformation(
                    "No persisted scheduler config found; using builder defaults."
                );
                return;
            }

            cfg.ManifestManagerEnabled = row.ManifestManagerEnabled;
            cfg.JobDispatcherEnabled = row.JobDispatcherEnabled;
            cfg.ManifestManagerPollingInterval = row.ManifestManagerPollingInterval;
            cfg.JobDispatcherPollingInterval = row.JobDispatcherPollingInterval;
            cfg.MaxActiveJobs = row.MaxActiveJobs;
            cfg.DefaultMaxRetries = row.DefaultMaxRetries;
            cfg.DefaultRetryDelay = row.DefaultRetryDelay;
            cfg.RetryBackoffMultiplier = row.RetryBackoffMultiplier;
            cfg.MaxRetryDelay = row.MaxRetryDelay;
            cfg.DefaultJobTimeout = row.DefaultJobTimeout;
            cfg.StalePendingTimeout = row.StalePendingTimeout;
            cfg.RecoverStuckJobsOnStartup = row.RecoverStuckJobsOnStartup;
            cfg.DeadLetterRetentionPeriod = row.DeadLetterRetentionPeriod;
            cfg.AutoPurgeDeadLetters = row.AutoPurgeDeadLetters;

            if (workerOpts is not null && row.LocalWorkerCount is { } wc)
                workerOpts.WorkerCount = wc;

            if (cfg.MetadataCleanup is not null)
            {
                if (row.MetadataCleanupInterval is { } interval)
                    cfg.MetadataCleanup.CleanupInterval = interval;
                if (row.MetadataCleanupRetention is { } retention)
                    cfg.MetadataCleanup.RetentionPeriod = retention;
            }

            _logger.LogInformation(
                "Applied persisted scheduler config (last updated {UpdatedAt}).",
                row.UpdatedAt
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to apply persisted scheduler config; using builder defaults."
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
