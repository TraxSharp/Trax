using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Scheduler.Configuration;

namespace Trax.Dashboard.Components.Pages.Settings;

public partial class ServerSettingsPage
{
    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    // ── Scheduler state ──
    private SchedulerConfiguration? _schedulerConfig;
    private bool _schedulerAvailable;

    private TimeSpanField _pollingInterval = new();
    private TimeSpanField _defaultRetryDelay = new();
    private TimeSpanField _maxRetryDelay = new();
    private TimeSpanField _defaultJobTimeout = new();
    private TimeSpanField _deadLetterRetentionPeriod = new();
    private TimeSpanField _cleanupInterval = new();
    private TimeSpanField _cleanupRetentionPeriod = new();

    private static readonly List<string> TimeUnits = ["seconds", "minutes", "hours", "days"];

    // Scheduler saved-state snapshots
    private bool _savedManifestManagerEnabled;
    private bool _savedJobDispatcherEnabled;
    private TimeSpan _savedPollingInterval;
    private int? _savedMaxActiveJobs;
    private int _savedDefaultMaxRetries;
    private TimeSpan _savedDefaultRetryDelay;
    private double _savedRetryBackoffMultiplier;
    private TimeSpan _savedMaxRetryDelay;
    private TimeSpan _savedDefaultJobTimeout;
    private bool _savedRecoverStuckJobsOnStartup;
    private TimeSpan _savedDeadLetterRetentionPeriod;
    private bool _savedAutoPurgeDeadLetters;
    private TimeSpan _savedCleanupInterval;
    private TimeSpan _savedCleanupRetentionPeriod;

    // ── Logging state ──
    private IConfigurationRoot? _configRoot;
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
            _schedulerConfig!.ManifestManagerEnabled != _savedManifestManagerEnabled
            || _schedulerConfig.JobDispatcherEnabled != _savedJobDispatcherEnabled
        );

    private bool IsPollingQueueDirty =>
        _schedulerAvailable
        && (
            _pollingInterval.ToTimeSpan() != _savedPollingInterval
            || _schedulerConfig!.MaxActiveJobs != _savedMaxActiveJobs
        );

    private bool IsRetryDirty =>
        _schedulerAvailable
        && (
            _schedulerConfig!.DefaultMaxRetries != _savedDefaultMaxRetries
            || _defaultRetryDelay.ToTimeSpan() != _savedDefaultRetryDelay
            || _schedulerConfig.RetryBackoffMultiplier != _savedRetryBackoffMultiplier
            || _maxRetryDelay.ToTimeSpan() != _savedMaxRetryDelay
        );

    private bool IsJobSettingsDirty =>
        _schedulerAvailable
        && (
            _defaultJobTimeout.ToTimeSpan() != _savedDefaultJobTimeout
            || _schedulerConfig!.RecoverStuckJobsOnStartup != _savedRecoverStuckJobsOnStartup
        );

    private bool IsDeadLetterDirty =>
        _schedulerAvailable
        && (
            _deadLetterRetentionPeriod.ToTimeSpan() != _savedDeadLetterRetentionPeriod
            || _schedulerConfig!.AutoPurgeDeadLetters != _savedAutoPurgeDeadLetters
        );

    private bool IsMetadataCleanupDirty =>
        _schedulerAvailable
        && _schedulerConfig?.MetadataCleanup is not null
        && (
            _cleanupInterval.ToTimeSpan() != _savedCleanupInterval
            || _cleanupRetentionPeriod.ToTimeSpan() != _savedCleanupRetentionPeriod
        );

    private bool IsLoggingDirty =>
        _loggingAvailable
        && _logLevels.Any(e =>
            e.Level != _savedLogLevels.GetValueOrDefault(e.Category, "Information")
        );

    protected override void OnInitialized()
    {
        // Scheduler
        _schedulerConfig = ServiceProvider.GetService<SchedulerConfiguration>();
        _schedulerAvailable = _schedulerConfig is not null;

        if (_schedulerAvailable)
        {
            LoadSchedulerFromConfig();
            SnapshotSchedulerState();
        }

        // Logging
        _configRoot = ServiceProvider.GetService<IConfiguration>() as IConfigurationRoot;
        var loggingSection = _configRoot?.GetSection("Logging:LogLevel");
        _loggingAvailable = _configRoot is not null && loggingSection?.GetChildren().Any() == true;

        if (_loggingAvailable)
        {
            LoadLogging();
            SnapshotLoggingState();
        }
    }

    // ── Scheduler helpers ──

    private void LoadSchedulerFromConfig()
    {
        _pollingInterval = TimeSpanField.FromTimeSpan(
            _schedulerConfig!.ManifestManagerPollingInterval
        );
        _defaultRetryDelay = TimeSpanField.FromTimeSpan(_schedulerConfig.DefaultRetryDelay);
        _maxRetryDelay = TimeSpanField.FromTimeSpan(_schedulerConfig.MaxRetryDelay);
        _defaultJobTimeout = TimeSpanField.FromTimeSpan(_schedulerConfig.DefaultJobTimeout);
        _deadLetterRetentionPeriod = TimeSpanField.FromTimeSpan(
            _schedulerConfig.DeadLetterRetentionPeriod
        );

        if (_schedulerConfig.MetadataCleanup is not null)
        {
            _cleanupInterval = TimeSpanField.FromTimeSpan(
                _schedulerConfig.MetadataCleanup.CleanupInterval
            );
            _cleanupRetentionPeriod = TimeSpanField.FromTimeSpan(
                _schedulerConfig.MetadataCleanup.RetentionPeriod
            );
        }
    }

    private void SaveScheduler()
    {
        if (_schedulerConfig is null)
            return;

        _schedulerConfig.ManifestManagerPollingInterval = _pollingInterval.ToTimeSpan();
        _schedulerConfig.JobDispatcherPollingInterval = _pollingInterval.ToTimeSpan();
        _schedulerConfig.DefaultRetryDelay = _defaultRetryDelay.ToTimeSpan();
        _schedulerConfig.MaxRetryDelay = _maxRetryDelay.ToTimeSpan();
        _schedulerConfig.DefaultJobTimeout = _defaultJobTimeout.ToTimeSpan();
        _schedulerConfig.DeadLetterRetentionPeriod = _deadLetterRetentionPeriod.ToTimeSpan();

        if (_schedulerConfig.MetadataCleanup is not null)
        {
            _schedulerConfig.MetadataCleanup.CleanupInterval = _cleanupInterval.ToTimeSpan();
            _schedulerConfig.MetadataCleanup.RetentionPeriod = _cleanupRetentionPeriod.ToTimeSpan();
        }

        SnapshotSchedulerState();
    }

    private void ResetSchedulerDefaults()
    {
        if (_schedulerConfig is null)
            return;

        _schedulerConfig.ManifestManagerEnabled = true;
        _schedulerConfig.JobDispatcherEnabled = true;
        _schedulerConfig.ManifestManagerPollingInterval = TimeSpan.FromSeconds(5);
        _schedulerConfig.JobDispatcherPollingInterval = TimeSpan.FromSeconds(5);
        _schedulerConfig.MaxActiveJobs = 10;
        _schedulerConfig.DefaultMaxRetries = 3;
        _schedulerConfig.DefaultRetryDelay = TimeSpan.FromMinutes(5);
        _schedulerConfig.RetryBackoffMultiplier = 2.0;
        _schedulerConfig.MaxRetryDelay = TimeSpan.FromHours(1);
        _schedulerConfig.DefaultJobTimeout = TimeSpan.FromMinutes(20);
        _schedulerConfig.RecoverStuckJobsOnStartup = true;
        _schedulerConfig.DeadLetterRetentionPeriod = TimeSpan.FromDays(30);
        _schedulerConfig.AutoPurgeDeadLetters = true;

        if (_schedulerConfig.MetadataCleanup is not null)
        {
            _schedulerConfig.MetadataCleanup.CleanupInterval = TimeSpan.FromMinutes(1);
            _schedulerConfig.MetadataCleanup.RetentionPeriod = TimeSpan.FromHours(1);
        }

        LoadSchedulerFromConfig();
        SnapshotSchedulerState();
    }

    private void SnapshotSchedulerState()
    {
        _savedManifestManagerEnabled = _schedulerConfig!.ManifestManagerEnabled;
        _savedJobDispatcherEnabled = _schedulerConfig.JobDispatcherEnabled;
        _savedPollingInterval = _schedulerConfig.ManifestManagerPollingInterval;
        _savedMaxActiveJobs = _schedulerConfig.MaxActiveJobs;
        _savedDefaultMaxRetries = _schedulerConfig.DefaultMaxRetries;
        _savedDefaultRetryDelay = _schedulerConfig.DefaultRetryDelay;
        _savedRetryBackoffMultiplier = _schedulerConfig.RetryBackoffMultiplier;
        _savedMaxRetryDelay = _schedulerConfig.MaxRetryDelay;
        _savedDefaultJobTimeout = _schedulerConfig.DefaultJobTimeout;
        _savedRecoverStuckJobsOnStartup = _schedulerConfig.RecoverStuckJobsOnStartup;
        _savedDeadLetterRetentionPeriod = _schedulerConfig.DeadLetterRetentionPeriod;
        _savedAutoPurgeDeadLetters = _schedulerConfig.AutoPurgeDeadLetters;

        if (_schedulerConfig.MetadataCleanup is not null)
        {
            _savedCleanupInterval = _schedulerConfig.MetadataCleanup.CleanupInterval;
            _savedCleanupRetentionPeriod = _schedulerConfig.MetadataCleanup.RetentionPeriod;
        }
    }

    // ── Logging helpers ──

    private void LoadLogging()
    {
        _logLevels = _configRoot!
            .GetSection("Logging:LogLevel")
            .GetChildren()
            .Select(section => new LogLevelEntry
            {
                Category = section.Key,
                Level = section.Value ?? "Information",
            })
            .OrderBy(e => e.Category == "Default" ? "" : e.Category)
            .ToList();
    }

    private void SaveLogging()
    {
        if (_configRoot is null)
            return;

        foreach (var entry in _logLevels)
            _configRoot[$"Logging:LogLevel:{entry.Category}"] = entry.Level;

        _configRoot.Reload();
        SnapshotLoggingState();
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

    private void Save()
    {
        if (_schedulerAvailable)
            SaveScheduler();

        if (_loggingAvailable)
            SaveLogging();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Settings Saved",
                Detail = "Server settings updated.",
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
                Detail = "All server settings have been reset to their default values.",
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
