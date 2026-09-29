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
/// settings survive restarts. Registered by <c>AddScheduler</c>; infrastructure not intended for
/// direct use.
/// </summary>
/// <remarks>
/// A persisted value outside <see cref="SchedulerConfigLimits"/> is skipped and logged, and the
/// rest of the row still applies. Failures are logged but never crash startup. If the table doesn't exist (e.g. an
/// older deployment skipped the migration), or no row is present, the in-memory
/// builder defaults remain in effect.
/// </remarks>
internal class SchedulerConfigBootstrapHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SchedulerConfigBootstrapHostedService> _logger;

    /// <summary>Created by the host through DI.</summary>
    /// <param name="services">Root provider; a scope is created from it for the one read at startup.</param>
    /// <param name="logger">Receives the applied, skipped and failed outcomes.</param>
    public SchedulerConfigBootstrapHostedService(
        IServiceProvider services,
        ILogger<SchedulerConfigBootstrapHostedService> logger
    )
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Reads the singleton <c>trax.scheduler_config</c> row once and copies its values onto
    /// <see cref="SchedulerConfiguration"/>, the <see cref="LocalWorkerOptions"/> worker count and
    /// the metadata cleanup interval and retention. Never throws: a missing row, a missing table or
    /// a database error is logged and leaves the builder's values in place.
    /// </summary>
    /// <param name="cancellationToken">Cancels the database read.</param>
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
            Apply(
                row.ManifestManagerPollingInterval,
                SchedulerConfigLimits.TimerInterval,
                nameof(row.ManifestManagerPollingInterval),
                v => cfg.ManifestManagerPollingInterval = v
            );
            Apply(
                row.JobDispatcherPollingInterval,
                SchedulerConfigLimits.TimerInterval,
                nameof(row.JobDispatcherPollingInterval),
                v => cfg.JobDispatcherPollingInterval = v
            );
            if (row.MaxActiveJobs is not { } maxActiveJobs)
                cfg.MaxActiveJobs = null;
            else
                Apply(
                    maxActiveJobs,
                    SchedulerConfigLimits.AtLeastOne,
                    nameof(row.MaxActiveJobs),
                    v => cfg.MaxActiveJobs = v
                );
            Apply(
                row.DefaultMaxRetries,
                SchedulerConfigLimits.NotNegative,
                nameof(row.DefaultMaxRetries),
                v => cfg.DefaultMaxRetries = v
            );
            Apply(
                row.DefaultRetryDelay,
                SchedulerConfigLimits.NonNegativeDuration,
                nameof(row.DefaultRetryDelay),
                v => cfg.DefaultRetryDelay = v
            );
            Apply(
                row.RetryBackoffMultiplier,
                SchedulerConfigLimits.BackoffMultiplier,
                nameof(row.RetryBackoffMultiplier),
                v => cfg.RetryBackoffMultiplier = v
            );
            Apply(
                row.MaxRetryDelay,
                SchedulerConfigLimits.NonNegativeDuration,
                nameof(row.MaxRetryDelay),
                v => cfg.MaxRetryDelay = v
            );
            Apply(
                row.DefaultJobTimeout,
                SchedulerConfigLimits.PositiveDuration,
                nameof(row.DefaultJobTimeout),
                v => cfg.DefaultJobTimeout = v
            );
            Apply(
                row.StalePendingTimeout,
                SchedulerConfigLimits.PositiveDuration,
                nameof(row.StalePendingTimeout),
                v => cfg.StalePendingTimeout = v
            );
            cfg.RecoverStuckJobsOnStartup = row.RecoverStuckJobsOnStartup;
            Apply(
                row.DeadLetterRetentionPeriod,
                SchedulerConfigLimits.NonNegativeDuration,
                nameof(row.DeadLetterRetentionPeriod),
                v => cfg.DeadLetterRetentionPeriod = v
            );
            cfg.AutoPurgeDeadLetters = row.AutoPurgeDeadLetters;

            if (workerOpts is not null && row.LocalWorkerCount is { } wc)
                Apply(
                    wc,
                    SchedulerConfigLimits.WorkerCount,
                    nameof(row.LocalWorkerCount),
                    v => workerOpts.WorkerCount = v
                );

            if (cfg.MetadataCleanup is { } cleanup)
            {
                if (row.MetadataCleanupInterval is { } interval)
                    Apply(
                        interval,
                        SchedulerConfigLimits.TimerInterval,
                        nameof(row.MetadataCleanupInterval),
                        v => cleanup.CleanupInterval = v
                    );
                if (row.MetadataCleanupRetention is { } retention)
                    Apply(
                        retention,
                        SchedulerConfigLimits.PositiveDuration,
                        nameof(row.MetadataCleanupRetention),
                        v => cleanup.RetentionPeriod = v
                    );
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

    /// <summary>
    /// Applies one persisted value, or skips it with a warning when it is outside the range the
    /// scheduler can run with (<see cref="SchedulerConfigLimits"/>). A row written before the
    /// operations service validated, or edited by hand, must not stop the host: a sub-millisecond
    /// polling interval, for one, makes the poller's timer throw and faults the host at boot. The
    /// builder's value stays in effect for a skipped field.
    /// </summary>
    private void Apply<T>(T value, Func<T?, string, string?> check, string name, Action<T> apply)
        where T : struct
    {
        if (check(value, name) is { } problem)
        {
            _logger.LogWarning(
                "Skipped the persisted scheduler setting {Setting} ({Value}): {Problem} The configured value stays in effect.",
                name,
                value,
                problem
            );
            return;
        }

        apply(value);
    }

    /// <summary>Does nothing; the service holds no resources after startup.</summary>
    /// <param name="cancellationToken">Unused.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
