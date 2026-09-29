using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radzen;
using Trax.Dashboard.Services.LogLevels;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Components.Pages.Settings;

/// <summary>
/// The server settings page, at <c>/trax/settings/server</c>: scheduler settings (polling, retries,
/// timeouts, dead letter and metadata cleanup, local workers) and log levels. Scheduler changes
/// save through the scheduler's operations service, the same path as the GraphQL scheduler config
/// mutation; log levels are applied to this process's logger filter. Each section appears only when
/// its services are registered. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class ServerSettingsPage
{
    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    // ── Scheduler state ──
    // The page edits a copy of the settings, never the live SchedulerConfiguration: the
    // operations service applies a save to the live settings and persists the row, and it
    // only does either for a value that differs from what is live. Binding the form to the
    // live object applied each edit before Save and left the service nothing to persist.
    private bool _schedulerAvailable;
    private bool _hasLocalWorkers;
    private bool _hasMetadataCleanup;
    private SchedulerConfigSnapshot _saved = null!;

    private bool _manifestManagerEnabled;
    private bool _jobDispatcherEnabled;
    private int? _maxActiveJobs;
    private int _workerCount;
    private int _defaultMaxRetries;
    private double _retryBackoffMultiplier;
    private bool _recoverStuckJobsOnStartup;
    private bool _autoPurgeDeadLetters;

    private TimeSpanField _pollingInterval = new();
    private TimeSpanField _defaultRetryDelay = new();
    private TimeSpanField _maxRetryDelay = new();
    private TimeSpanField _defaultJobTimeout = new();
    private TimeSpanField _stalePendingTimeout = new();
    private TimeSpanField _deadLetterRetentionPeriod = new();
    private TimeSpanField _cleanupInterval = new();
    private TimeSpanField _cleanupRetentionPeriod = new();

    private static readonly List<string> TimeUnits = ["seconds", "minutes", "hours", "days"];

    // ── Logging state ──
    private IConfiguration? _configuration;
    private DashboardLogLevelOverrides? _logLevelOverrides;
    private bool _loggingAvailable;
    private List<LogLevelEntry> _logLevels = [];
    private Dictionary<string, string> _savedLogLevels = new();

    private static readonly List<string> LogLevelValues =
    [
        "Trace",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Critical",
        "None",
    ];

    // ── Dirty tracking ──
    private bool IsAdminTrainsDirty =>
        _schedulerAvailable
        && (
            _manifestManagerEnabled != _saved.ManifestManagerEnabled
            || _jobDispatcherEnabled != _saved.JobDispatcherEnabled
        );

    private bool IsPollingQueueDirty =>
        _schedulerAvailable
        && (
            _pollingInterval.ToTimeSpan() != _saved.ManifestManagerPollingInterval
            || _maxActiveJobs != _saved.MaxActiveJobs
            || (_hasLocalWorkers && _workerCount != _saved.LocalWorkerCount)
        );

    private bool IsRetryDirty =>
        _schedulerAvailable
        && (
            _defaultMaxRetries != _saved.DefaultMaxRetries
            || _defaultRetryDelay.ToTimeSpan() != _saved.DefaultRetryDelay
            || _retryBackoffMultiplier != _saved.RetryBackoffMultiplier
            || _maxRetryDelay.ToTimeSpan() != _saved.MaxRetryDelay
        );

    private bool IsJobSettingsDirty =>
        _schedulerAvailable
        && (
            _defaultJobTimeout.ToTimeSpan() != _saved.DefaultJobTimeout
            || _stalePendingTimeout.ToTimeSpan() != _saved.StalePendingTimeout
            || _recoverStuckJobsOnStartup != _saved.RecoverStuckJobsOnStartup
        );

    private bool IsDeadLetterDirty =>
        _schedulerAvailable
        && (
            _deadLetterRetentionPeriod.ToTimeSpan() != _saved.DeadLetterRetentionPeriod
            || _autoPurgeDeadLetters != _saved.AutoPurgeDeadLetters
        );

    private bool IsMetadataCleanupDirty =>
        _schedulerAvailable
        && _hasMetadataCleanup
        && (
            _cleanupInterval.ToTimeSpan() != _saved.MetadataCleanupInterval
            || _cleanupRetentionPeriod.ToTimeSpan() != _saved.MetadataCleanupRetention
        );

    private bool IsLoggingDirty =>
        _loggingAvailable
        && _logLevels.Any(e =>
            e.Level != _savedLogLevels.GetValueOrDefault(e.Category, "Information")
        );

    /// <summary>
    /// Loads a copy of the current scheduler settings into the form when a scheduler is
    /// registered, and the configured <c>Logging:LogLevel</c> categories when there are any.
    /// </summary>
    protected override void OnInitialized()
    {
        // Scheduler
        _schedulerAvailable = ServiceProvider.GetService<SchedulerConfiguration>() is not null;

        if (_schedulerAvailable)
        {
            _saved = OperationsService.GetSchedulerConfig();
            _hasLocalWorkers = _saved.LocalWorkerCount is not null;
            _hasMetadataCleanup = _saved.MetadataCleanupInterval is not null;
            LoadSchedulerForm(_saved);
        }

        // Logging
        _configuration = ServiceProvider.GetService<IConfiguration>();
        _logLevelOverrides = ServiceProvider.GetService<DashboardLogLevelOverrides>();
        _loggingAvailable =
            _configuration is not null
            && _logLevelOverrides is not null
            && _configuration.GetSection("Logging:LogLevel").GetChildren().Any();

        if (_loggingAvailable)
        {
            LoadLogging();
            SnapshotLoggingState();
        }
    }

    // ── Scheduler helpers ──

    private void LoadSchedulerForm(SchedulerConfigSnapshot settings)
    {
        _manifestManagerEnabled = settings.ManifestManagerEnabled;
        _jobDispatcherEnabled = settings.JobDispatcherEnabled;
        _pollingInterval = TimeSpanField.FromTimeSpan(settings.ManifestManagerPollingInterval);
        _maxActiveJobs = settings.MaxActiveJobs;
        _workerCount = settings.LocalWorkerCount ?? 0;
        _defaultMaxRetries = settings.DefaultMaxRetries;
        _defaultRetryDelay = TimeSpanField.FromTimeSpan(settings.DefaultRetryDelay);
        _retryBackoffMultiplier = settings.RetryBackoffMultiplier;
        _maxRetryDelay = TimeSpanField.FromTimeSpan(settings.MaxRetryDelay);
        _defaultJobTimeout = TimeSpanField.FromTimeSpan(settings.DefaultJobTimeout);
        _stalePendingTimeout = TimeSpanField.FromTimeSpan(settings.StalePendingTimeout);
        _recoverStuckJobsOnStartup = settings.RecoverStuckJobsOnStartup;
        _deadLetterRetentionPeriod = TimeSpanField.FromTimeSpan(settings.DeadLetterRetentionPeriod);
        _autoPurgeDeadLetters = settings.AutoPurgeDeadLetters;

        if (settings.MetadataCleanupInterval is { } interval)
            _cleanupInterval = TimeSpanField.FromTimeSpan(interval);
        if (settings.MetadataCleanupRetention is { } retention)
            _cleanupRetentionPeriod = TimeSpanField.FromTimeSpan(retention);
    }

    /// <summary>
    /// Writes the form through the shared operations service, so the dashboard save and the
    /// GraphQL <c>updateSchedulerConfig</c> mutation make the same write. The service applies
    /// the values that differ to the live settings and persists the row. The dispatcher's
    /// polling interval is not sent: the page has no field for it, and its one polling field
    /// is the ManifestManager's.
    /// </summary>
    private async Task<OperationResult> SaveScheduler()
    {
        var input = new UpdateSchedulerConfigInput(
            ManifestManagerEnabled: _manifestManagerEnabled,
            JobDispatcherEnabled: _jobDispatcherEnabled,
            ManifestManagerPollingInterval: _pollingInterval.ToTimeSpan(),
            MaxActiveJobs: _maxActiveJobs,
            ClearMaxActiveJobs: _maxActiveJobs is null,
            DefaultMaxRetries: _defaultMaxRetries,
            DefaultRetryDelay: _defaultRetryDelay.ToTimeSpan(),
            RetryBackoffMultiplier: _retryBackoffMultiplier,
            MaxRetryDelay: _maxRetryDelay.ToTimeSpan(),
            DefaultJobTimeout: _defaultJobTimeout.ToTimeSpan(),
            StalePendingTimeout: _stalePendingTimeout.ToTimeSpan(),
            RecoverStuckJobsOnStartup: _recoverStuckJobsOnStartup,
            DeadLetterRetentionPeriod: _deadLetterRetentionPeriod.ToTimeSpan(),
            AutoPurgeDeadLetters: _autoPurgeDeadLetters,
            LocalWorkerCount: _hasLocalWorkers ? _workerCount : null,
            MetadataCleanupInterval: _hasMetadataCleanup ? _cleanupInterval.ToTimeSpan() : null,
            MetadataCleanupRetention: _hasMetadataCleanup
                ? _cleanupRetentionPeriod.ToTimeSpan()
                : null
        );

        var result = await OperationsService.UpdateSchedulerConfigAsync(
            input,
            CancellationToken.None
        );

        if (result.Success)
            _saved = OperationsService.GetSchedulerConfig();

        return result;
    }

    /// <summary>
    /// Fills the form with the library defaults for every setting the page shows. Nothing is
    /// applied until Save, which writes them through the service like any other edit.
    /// </summary>
    private void ResetSchedulerDefaults()
    {
        var defaults = new SchedulerConfiguration();
        var cleanupDefaults = new MetadataCleanupConfiguration();

        _manifestManagerEnabled = defaults.ManifestManagerEnabled;
        _jobDispatcherEnabled = defaults.JobDispatcherEnabled;
        _pollingInterval = TimeSpanField.FromTimeSpan(defaults.ManifestManagerPollingInterval);
        _maxActiveJobs = defaults.MaxActiveJobs;
        _workerCount = new LocalWorkerOptions().WorkerCount;
        _defaultMaxRetries = defaults.DefaultMaxRetries;
        _defaultRetryDelay = TimeSpanField.FromTimeSpan(defaults.DefaultRetryDelay);
        _retryBackoffMultiplier = defaults.RetryBackoffMultiplier;
        _maxRetryDelay = TimeSpanField.FromTimeSpan(defaults.MaxRetryDelay);
        _defaultJobTimeout = TimeSpanField.FromTimeSpan(defaults.DefaultJobTimeout);
        _stalePendingTimeout = TimeSpanField.FromTimeSpan(defaults.StalePendingTimeout);
        _recoverStuckJobsOnStartup = defaults.RecoverStuckJobsOnStartup;
        _deadLetterRetentionPeriod = TimeSpanField.FromTimeSpan(defaults.DeadLetterRetentionPeriod);
        _autoPurgeDeadLetters = defaults.AutoPurgeDeadLetters;
        _cleanupInterval = TimeSpanField.FromTimeSpan(cleanupDefaults.CleanupInterval);
        _cleanupRetentionPeriod = TimeSpanField.FromTimeSpan(cleanupDefaults.RetentionPeriod);
    }

    // ── Logging helpers ──

    /// <summary>
    /// One row per category the host configures under <c>Logging:LogLevel</c>, showing a level
    /// saved from this page over the configured one.
    /// </summary>
    private void LoadLogging()
    {
        var overrides = _logLevelOverrides!.Levels;
        _logLevels = _configuration!
            .GetSection("Logging:LogLevel")
            .GetChildren()
            .Select(section => new LogLevelEntry
            {
                Category = section.Key,
                Level = overrides.TryGetValue(section.Key, out var saved)
                    ? saved.ToString()
                    : section.Value ?? "Information",
            })
            .OrderBy(e =>
                e.Category == DashboardLogLevelOverrides.DefaultCategory ? "" : e.Category
            )
            .ToList();
    }

    /// <summary>
    /// Applies the changed levels to the host's logger filters, then reads back the level each
    /// category is filtered at. Returns the categories where that is not the level saved, which
    /// happens when the host sets the filter itself after the dashboard.
    /// </summary>
    private List<string> SaveLogging()
    {
        var changed = _logLevels
            .Where(e => e.Level != _savedLogLevels.GetValueOrDefault(e.Category, "Information"))
            .Select(e => KeyValuePair.Create(e.Category, Enum.Parse<LogLevel>(e.Level)))
            .ToList();
        if (changed.Count == 0)
            return [];

        _logLevelOverrides!.Set(changed);

        var filters = ServiceProvider
            .GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>()
            .CurrentValue;
        var notApplied = changed
            .Where(c => DashboardLogLevelOverrides.EffectiveLevel(filters, c.Key) != c.Value)
            .Select(c => c.Key)
            .ToList();

        SnapshotLoggingState();
        return notApplied;
    }

    private void ResetLoggingDefaults()
    {
        foreach (var entry in _logLevels)
            entry.Level = _savedLogLevels.GetValueOrDefault(entry.Category, "Information");
    }

    private void SnapshotLoggingState()
    {
        _savedLogLevels = _logLevels.ToDictionary(e => e.Category, e => e.Level);
    }

    // ── Combined actions ──

    private async Task Save()
    {
        var details = new List<string>();

        if (_schedulerAvailable)
        {
            OperationResult result;
            try
            {
                result = await SaveScheduler();
            }
            catch (Exception ex)
            {
                result = new OperationResult(false, Message: ex.Message);
            }

            if (!result.Success)
            {
                NotificationService.Notify(
                    new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "Scheduler settings not saved",
                        Detail = result.Message ?? "The operations service refused the change.",
                        Duration = 8000,
                    }
                );
                return;
            }

            details.Add(result.Message ?? "Scheduler settings saved.");
        }

        if (_loggingAvailable)
        {
            var notApplied = SaveLogging();
            if (notApplied.Count > 0)
            {
                NotificationService.Notify(
                    new NotificationMessage
                    {
                        Severity = NotificationSeverity.Error,
                        Summary = "Log levels not applied",
                        Detail =
                            "The host sets these categories' levels after the dashboard, so the "
                            + $"saved level is not the one in force: {string.Join(", ", notApplied)}.",
                        Duration = 8000,
                    }
                );
                return;
            }

            details.Add("Log levels updated.");
        }

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Settings Saved",
                Detail = string.Join(" ", details),
                Duration = 4000,
            }
        );
    }

    private void ResetDefaults()
    {
        if (_schedulerAvailable)
            ResetSchedulerDefaults();

        if (_loggingAvailable)
            ResetLoggingDefaults();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "Defaults Restored",
                Detail = "The form now holds the default values. Save to apply them.",
                Duration = 4000,
            }
        );
    }

    // ── Inner types ──

    private class TimeSpanField
    {
        public double Value { get; set; }
        public string Unit { get; set; } = "seconds";

        public TimeSpan ToTimeSpan() =>
            Unit switch
            {
                "days" => TimeSpan.FromDays(Value),
                "hours" => TimeSpan.FromHours(Value),
                "minutes" => TimeSpan.FromMinutes(Value),
                _ => TimeSpan.FromSeconds(Value),
            };

        public static TimeSpanField FromTimeSpan(TimeSpan ts)
        {
            if (ts.TotalDays >= 1 && ts.TotalDays == Math.Floor(ts.TotalDays))
                return new() { Value = ts.TotalDays, Unit = "days" };
            if (ts.TotalHours >= 1 && ts.TotalHours == Math.Floor(ts.TotalHours))
                return new() { Value = ts.TotalHours, Unit = "hours" };
            if (ts.TotalMinutes >= 1 && ts.TotalMinutes == Math.Floor(ts.TotalMinutes))
                return new() { Value = ts.TotalMinutes, Unit = "minutes" };
            return new() { Value = ts.TotalSeconds, Unit = "seconds" };
        }
    }

    private class LogLevelEntry
    {
        public required string Category { get; init; }
        public string Level { get; set; } = "Information";
    }
}
