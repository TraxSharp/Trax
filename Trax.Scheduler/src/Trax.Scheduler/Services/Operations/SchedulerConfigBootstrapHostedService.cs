using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Utilities;
using SchedulerConfigRow = Trax.Effect.Models.SchedulerConfig.SchedulerConfig;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Applies the persisted <c>trax.scheduler_config</c> row to the in-memory
/// <see cref="SchedulerConfiguration"/> singleton (and <see cref="LocalWorkerOptions"/> /
/// <c>MetadataCleanup</c> when registered): once at startup, so settings survive restarts, and
/// then every <see cref="SchedulerConfiguration.SettingsRefreshInterval"/>, so a save made on any
/// host, including an API-only one, reaches this scheduler within seconds without a restart.
/// Registered by <c>AddScheduler</c>; infrastructure not intended for direct use.
/// </summary>
/// <remarks>
/// <para>
/// Each setting the row names in its <c>overrides</c> replaces the value configured in code; every
/// other setting keeps the configured value, so a later change in code applies to it. A row
/// written before <c>overrides</c> existed sets every setting it has a column for. The configured
/// values are captured when the service starts, so a row that is deleted at runtime, or stops
/// naming a setting, returns it to them.
/// </para>
/// <para>
/// A persisted value outside <see cref="SchedulerConfigLimits"/> is skipped and logged, and the
/// configured value stands for it while the rest of the row still applies. Failures are logged
/// but never crash startup or stop the refresh. If the table doesn't exist (e.g. an older
/// deployment skipped the migration), or no row is present, the configured values remain in effect.
/// </para>
/// </remarks>
internal class SchedulerConfigBootstrapHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SchedulerConfigBootstrapHostedService> _logger;

    private IReadOnlyDictionary<string, object?>? _configured;
    private bool _rowApplied;
    private DateTime _appliedUpdatedAt;
    private bool _refreshFailing;
    private CancellationTokenSource? _stopping;
    private Task? _refreshLoop;
    private int _completedRefreshes;

    /// <summary>How many refreshes after startup have finished; lets tests wait for one.</summary>
    internal int CompletedRefreshes => Volatile.Read(ref _completedRefreshes);

    /// <summary>Created by the host through DI.</summary>
    /// <param name="services">Root provider; a scope is created from it for each read.</param>
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
    /// Captures the configured values, applies the persisted row once, then starts the refresh
    /// that keeps applying it. Never throws: a missing row, a missing table or a database error is
    /// logged and leaves the configured values in place. The refresh starts even when the first
    /// read fails, so a row that becomes readable later (the database was briefly unreachable, or
    /// the table was not migrated yet) is still applied without a restart.
    /// </summary>
    /// <param name="cancellationToken">Cancels the startup read.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SchedulerConfiguration configuration;
        try
        {
            configuration = _services.GetRequiredService<SchedulerConfiguration>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to apply persisted scheduler config; using builder defaults."
            );
            return;
        }

        try
        {
            await RefreshAsync(startup: true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to apply persisted scheduler config; using builder defaults until it can be read."
            );
            // The refresh logs once per outage; this was the first failure of this one.
            _refreshFailing = true;
        }

        if (configuration.SettingsRefreshInterval <= TimeSpan.Zero || _refreshLoop is not null)
            return;

        _stopping = new CancellationTokenSource();
        _refreshLoop = RefreshLoopAsync(configuration, _stopping.Token);
    }

    /// <summary>Stops the refresh.</summary>
    /// <param name="cancellationToken">Bounds the wait for the refresh to finish.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null || _refreshLoop is null)
            return;

        await _stopping.CancelAsync();
        await _refreshLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the refresh's cancellation source.</summary>
    public void Dispose() => _stopping?.Dispose();

    private async Task RefreshLoopAsync(
        SchedulerConfiguration configuration,
        CancellationToken stoppingToken
    )
    {
        // Yield so StartAsync returns before the first wait.
        await Task.Yield();

        while (
            await PollingDelay.WaitAsync(
                () =>
                    configuration.SettingsRefreshInterval > TimeSpan.Zero
                        ? configuration.SettingsRefreshInterval
                        : TimeSpan.FromSeconds(5),
                stoppingToken
            )
        )
        {
            try
            {
                await RefreshAsync(startup: false, stoppingToken);
                _refreshFailing = false;
                Interlocked.Increment(ref _completedRefreshes);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Once per outage rather than once per check.
                if (!_refreshFailing)
                    _logger.LogWarning(
                        ex,
                        "Could not read the persisted scheduler config; the settings in effect stay until it can be read."
                    );
                _refreshFailing = true;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reads the row and applies it when it changed since the last read: a new or updated row
    /// applies its values, a deleted row returns every setting to the configured value.
    /// </summary>
    private async Task RefreshAsync(bool startup, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var target = new SchedulerSettingsTarget(
            scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>(),
            scope.ServiceProvider.GetService<LocalWorkerOptions>()
        );
        _configured ??= SchedulerSettings.Capture(target);

        using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db
            .SchedulerConfigs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == SchedulerConfigRow.SingletonId, cancellationToken);

        if (row is null)
        {
            if (startup)
                _logger.LogInformation(
                    "No persisted scheduler config found; using builder defaults."
                );
            else if (_rowApplied)
            {
                // Only a row that was applied and then deleted returns the settings to the
                // configured values. With no row ever applied there is nothing to undo, and
                // resetting here would revert every runtime change on each refresh.
                Apply(null, target);
                _logger.LogInformation(
                    "The persisted scheduler config was removed; the configured settings apply again."
                );
            }
            _rowApplied = false;
            return;
        }

        if (_rowApplied && row.UpdatedAt == _appliedUpdatedAt)
            return;

        Apply(row, target);
        _rowApplied = true;
        _appliedUpdatedAt = row.UpdatedAt;

        _logger.LogInformation(
            "Applied persisted scheduler config (last updated {UpdatedAt}).",
            row.UpdatedAt
        );
    }

    /// <summary>
    /// Sets each setting this host has to the row's value when the row sets one the scheduler can
    /// run with, and to the configured value otherwise. A value outside
    /// <see cref="SchedulerConfigLimits"/> (a row written before the operations service validated,
    /// or edited by hand) is skipped with a warning: a sub-millisecond polling interval, for one,
    /// must not stop the host.
    /// </summary>
    private void Apply(SchedulerConfigRow? row, SchedulerSettingsTarget target)
    {
        foreach (var setting in SchedulerSettings.All)
        {
            if (
                !setting.AppliesTo(target) || !_configured!.TryGetValue(setting.Name, out var value)
            )
                continue;

            var configured = value;
            var fromRow = false;
            if (row is not null && setting.TryReadRow(row, out var stored))
            {
                if (setting.Check(stored) is { } problem)
                    _logger.LogWarning(
                        "Skipped the persisted scheduler setting {Setting} ({Value}): {Problem} The configured value stays in effect.",
                        setting.Name,
                        stored,
                        problem
                    );
                else
                {
                    value = stored;
                    fromRow = true;
                }
            }

            if (!Equals(setting.ReadLive(target), value))
                setting.WriteLive(target, value);

            if (!fromRow || Equals(value, configured))
                continue;

            // A saved value replacing a different one in code is what a save is for, but a
            // deploy that changes that setting in code then has no effect, so say so.
            var effective = setting.ReadLive(target);
            if (Equals(effective, value))
                _logger.LogWarning(
                    "The saved scheduler setting {Setting} ({Saved}) replaces the value configured in code ({Configured}).",
                    setting.Name,
                    value,
                    configured
                );
            else
                _logger.LogWarning(
                    "The saved scheduler setting {Setting} ({Saved}) is less cautious than the value configured in code ({Configured}), so the scheduler runs with {Effective}.",
                    setting.Name,
                    value,
                    configured,
                    effective
                );
        }
    }
}
