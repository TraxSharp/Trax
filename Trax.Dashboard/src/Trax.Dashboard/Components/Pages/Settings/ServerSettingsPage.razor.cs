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

    // Each duration field carries the range the operations service accepts for its setting, so
    // the form can refuse a value before Save instead of sending one the service will refuse, and
    // never builds a TimeSpan too large to exist. The service stays the authority on the ranges.
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxTimerInterval = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromDays(3650);

    private readonly TimeSpanField _pollingInterval = new(OneSecond, MaxTimerInterval);
    private readonly TimeSpanField _failureCountWindow = new(OneSecond, MaxDuration);
    private readonly TimeSpanField _defaultRetryDelay = new(TimeSpan.Zero, MaxDuration);
    private readonly TimeSpanField _maxRetryDelay = new(TimeSpan.Zero, MaxDuration);
    private readonly TimeSpanField _defaultJobTimeout = new(OneSecond, MaxDuration);
    private readonly TimeSpanField _stalePendingTimeout = new(OneSecond, MaxDuration);
    private readonly TimeSpanField _deadLetterRetentionPeriod = new(TimeSpan.Zero, MaxDuration);
    private readonly TimeSpanField _cleanupInterval = new(OneSecond, MaxTimerInterval);
    private readonly TimeSpanField _cleanupRetentionPeriod = new(OneSecond, MaxDuration);

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

    // ── Validation ──
    private bool HasInvalidField =>
        _schedulerAvailable
        && new[]
        {
            _pollingInterval,
            _failureCountWindow,
            _defaultRetryDelay,
            _maxRetryDelay,
            _defaultJobTimeout,
            _stalePendingTimeout,
            _deadLetterRetentionPeriod,
        }
            .Concat(
                _hasMetadataCleanup
                    ? new[] { _cleanupInterval, _cleanupRetentionPeriod }
                    : Array.Empty<TimeSpanField>()
            )
            .Any(f => !f.IsValid);

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
            || _failureCountWindow.ToTimeSpan() != _saved.FailureCountWindow
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
        _pollingInterval.Set(settings.ManifestManagerPollingInterval);
        _maxActiveJobs = settings.MaxActiveJobs;
        _workerCount = settings.LocalWorkerCount ?? 0;
        _defaultMaxRetries = settings.DefaultMaxRetries;
        _failureCountWindow.Set(settings.FailureCountWindow);
        _defaultRetryDelay.Set(settings.DefaultRetryDelay);
        _retryBackoffMultiplier = settings.RetryBackoffMultiplier;
        _maxRetryDelay.Set(settings.MaxRetryDelay);
        _defaultJobTimeout.Set(settings.DefaultJobTimeout);
        _stalePendingTimeout.Set(settings.StalePendingTimeout);
        _recoverStuckJobsOnStartup = settings.RecoverStuckJobsOnStartup;
        _deadLetterRetentionPeriod.Set(settings.DeadLetterRetentionPeriod);
        _autoPurgeDeadLetters = settings.AutoPurgeDeadLetters;

        if (settings.MetadataCleanupInterval is { } interval)
            _cleanupInterval.Set(interval);
        if (settings.MetadataCleanupRetention is { } retention)
            _cleanupRetentionPeriod.Set(retention);
    }

    /// <summary>
    /// Reads the settings in force from the operations service and shows them in the form, so the
    /// form never holds a value some other writer (another operator, the GraphQL mutation) has
    /// since replaced.
    /// </summary>
    private void ReloadSchedulerForm()
    {
        _saved = OperationsService.GetSchedulerConfig();
        LoadSchedulerForm(_saved);
    }

    /// <summary>
    /// Writes the fields the operator changed through the shared operations service, so the
    /// dashboard save and the GraphQL <c>updateSchedulerConfig</c> mutation make the same write.
    /// A field left alone is not sent: sending it would pin it in the stored row, and would put
    /// back the value this page loaded over a change another writer made since. The dispatcher's
    /// polling interval is never sent: the page has no field for it, and its one polling field is
    /// the ManifestManager's. Callers check <see cref="HasInvalidField"/> first.
    /// </summary>
    private async Task<OperationResult> SaveScheduler()
    {
        var maxActiveJobsChanged = _maxActiveJobs != _saved.MaxActiveJobs;
        var input = new UpdateSchedulerConfigInput(
            ManifestManagerEnabled: Changed(_manifestManagerEnabled, _saved.ManifestManagerEnabled),
            JobDispatcherEnabled: Changed(_jobDispatcherEnabled, _saved.JobDispatcherEnabled),
            ManifestManagerPollingInterval: Changed(
                _pollingInterval,
                _saved.ManifestManagerPollingInterval
            ),
            MaxActiveJobs: maxActiveJobsChanged ? _maxActiveJobs : null,
            ClearMaxActiveJobs: maxActiveJobsChanged && _maxActiveJobs is null,
            DefaultMaxRetries: Changed(_defaultMaxRetries, _saved.DefaultMaxRetries),
            DefaultRetryDelay: Changed(_defaultRetryDelay, _saved.DefaultRetryDelay),
            RetryBackoffMultiplier: Changed(_retryBackoffMultiplier, _saved.RetryBackoffMultiplier),
            MaxRetryDelay: Changed(_maxRetryDelay, _saved.MaxRetryDelay),
            DefaultJobTimeout: Changed(_defaultJobTimeout, _saved.DefaultJobTimeout),
            StalePendingTimeout: Changed(_stalePendingTimeout, _saved.StalePendingTimeout),
            RecoverStuckJobsOnStartup: Changed(
                _recoverStuckJobsOnStartup,
                _saved.RecoverStuckJobsOnStartup
            ),
            DeadLetterRetentionPeriod: Changed(
                _deadLetterRetentionPeriod,
                _saved.DeadLetterRetentionPeriod
            ),
            AutoPurgeDeadLetters: Changed(_autoPurgeDeadLetters, _saved.AutoPurgeDeadLetters),
            LocalWorkerCount: _hasLocalWorkers
                ? Changed(_workerCount, _saved.LocalWorkerCount ?? 0)
                : null,
            MetadataCleanupInterval: _hasMetadataCleanup
                ? Changed(_cleanupInterval, _saved.MetadataCleanupInterval)
                : null,
            MetadataCleanupRetention: _hasMetadataCleanup
                ? Changed(_cleanupRetentionPeriod, _saved.MetadataCleanupRetention)
                : null
        )
        {
            FailureCountWindow = Changed(_failureCountWindow, _saved.FailureCountWindow),
        };

        var result = await OperationsService.UpdateSchedulerConfigAsync(
            input,
            CancellationToken.None
        );

        if (result.Success)
            ReloadSchedulerForm();

        return result;
    }

    private static T? Changed<T>(T value, T loaded)
        where T : struct => EqualityComparer<T>.Default.Equals(value, loaded) ? null : value;

    private static TimeSpan? Changed(TimeSpanField field, TimeSpan? loaded) =>
        field.ToTimeSpan() is { } value && value != loaded ? value : null;

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

    private void DiscardLoggingChanges()
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
        if (HasInvalidField)
        {
            NotificationService.Notify(
                new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "Settings not saved",
                    Detail = "A duration is out of range. Correct the highlighted field first.",
                    Duration = 8000,
                }
            );
            return;
        }

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

    /// <summary>
    /// Drops every unsaved edit: the scheduler fields are read again from the operations service,
    /// so they show the settings in force now, and each log level goes back to the one last saved.
    /// There is no way to return to the values the host's code configured: once a setting is
    /// saved, the stored value is what every scheduler host runs with.
    /// </summary>
    private void DiscardChanges()
    {
        if (_schedulerAvailable)
            ReloadSchedulerForm();

        if (_loggingAvailable)
            DiscardLoggingChanges();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "Changes Discarded",
                Detail = "The form shows the settings in force now.",
                Duration = 4000,
            }
        );
    }

    // ── Inner types ──

    /// <summary>
    /// A duration edited as a number and a unit, bounded by the range its setting accepts. A value
    /// outside that range, including one too large for a <see cref="TimeSpan"/>, is invalid rather
    /// than an exception, because the dirty checks read it on every render. The box itself is not
    /// given a maximum: Radzen would clamp to it silently, including when the unit changes, and
    /// save a value the operator never typed.
    /// </summary>
    private sealed class TimeSpanField(TimeSpan min, TimeSpan max)
    {
        public double Value { get; set; }
        public string Unit { get; set; } = "seconds";

        public bool IsValid => ToTimeSpan() is not null;

        public string InvalidText =>
            $"Enter a duration between {Describe(min)} and {Describe(max)}.";

        private double UnitSeconds =>
            Unit switch
            {
                "days" => 86400,
                "hours" => 3600,
                "minutes" => 60,
                _ => 1,
            };

        /// <summary>The duration, or null when it is outside the setting's range.</summary>
        public TimeSpan? ToTimeSpan()
        {
            if (!double.IsFinite(Value))
                return null;

            var seconds = Value * UnitSeconds;
            if (
                !double.IsFinite(seconds)
                || seconds > max.TotalSeconds
                || seconds < min.TotalSeconds
            )
                return null;

            return Unit switch
            {
                "days" => TimeSpan.FromDays(Value),
                "hours" => TimeSpan.FromHours(Value),
                "minutes" => TimeSpan.FromMinutes(Value),
                _ => TimeSpan.FromSeconds(Value),
            };
        }

        public void Set(TimeSpan ts)
        {
            (Value, Unit) = Split(ts);
        }

        private static (double Value, string Unit) Split(TimeSpan ts)
        {
            if (ts.TotalDays >= 1 && ts.TotalDays == Math.Floor(ts.TotalDays))
                return (ts.TotalDays, "days");
            if (ts.TotalHours >= 1 && ts.TotalHours == Math.Floor(ts.TotalHours))
                return (ts.TotalHours, "hours");
            if (ts.TotalMinutes >= 1 && ts.TotalMinutes == Math.Floor(ts.TotalMinutes))
                return (ts.TotalMinutes, "minutes");
            return (ts.TotalSeconds, "seconds");
        }

        private static string Describe(TimeSpan ts)
        {
            var (value, unit) = Split(ts);
            return $"{value} {unit}";
        }
    }

    private class LogLevelEntry
    {
        public required string Category { get; init; }
        public string Level { get; set; } = "Information";
    }
}
