using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for individual scheduled manifests.
/// </summary>
/// <remarks>
/// ManifestOptions provides fine-grained control over individual job behavior.
/// Default values are applied when scheduling a job, and can be overridden
/// per item through the <c>configureEach</c> callback of
/// <see cref="Services.TraxScheduler.ITraxScheduler.ScheduleManyAsync"/>. For a single manifest, set
/// the same values through <see cref="ScheduleOptions"/>.
/// </remarks>
/// <example>
/// <code>
/// await scheduler.ScheduleManyAsync&lt;ISyncTableTrain, SyncTableInput, Unit, string&gt;(
///     tables,
///     table => ($"sync-{table}", new SyncTableInput { Table = table }),
///     Every.Minutes(5),
///     configureEach: (table, opts) =>
///     {
///         opts.MaxRetries = table == "orders" ? 5 : 3;
///         opts.Timeout = TimeSpan.FromMinutes(30);
///     });
/// </code>
/// </example>
public class ManifestOptions
{
    /// <summary>
    /// Gets or sets whether the manifest is enabled for scheduling.
    /// </summary>
    /// <remarks>
    /// When false, the ManifestManager will skip this manifest during polling.
    /// This allows pausing jobs without deleting them. Defaults to true. Written to an existing
    /// manifest only when set: left unset, a re-seed keeps the manifest's current state, including
    /// a runtime disable.
    /// </remarks>
    public bool IsEnabled
    {
        get => _isEnabled ?? true;
        set => _isEnabled = value;
    }

    internal bool? _isEnabled;

    /// <summary>
    /// Gets or sets how many times a failed run is retried before the manifest is dead-lettered.
    /// </summary>
    /// <remarks>
    /// The count is of retries after the first run: each retry creates a new Metadata record, and
    /// the failure after the last retry moves the manifest to the dead letter queue for manual
    /// intervention. 0 runs once and dead-letters on the first failure. Left unset, the manifest
    /// takes the scheduler's <c>DefaultMaxRetries</c> (3, four attempts, unless configured);
    /// inside a <c>configureEach</c> callback it already reads the batch's value. Failures count
    /// within <see cref="SchedulerConfiguration.FailureCountWindow"/> and after the manifest's
    /// latest resolved dead letter, or within <see cref="FailureWindow"/> when it is set. Written
    /// to an existing manifest only when set: left unset, a re-seed keeps the manifest's current
    /// value, including one an operator changed at runtime.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value set is negative.</exception>
    public int MaxRetries
    {
        get => _maxRetries ?? _defaultMaxRetries ?? 3;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxRetries = value;
        }
    }

    /// <summary>The retries the code states, or null when it states none.</summary>
    internal int? _maxRetries;

    /// <summary>The scheduler's <c>DefaultMaxRetries</c>, which a new manifest takes when none is stated.</summary>
    internal int? _defaultMaxRetries;

    /// <summary>
    /// Gets or sets the timeout for job execution.
    /// </summary>
    /// <remarks>
    /// If a job is in "InProgress" state for longer than this duration,
    /// it may be considered stuck and subject to recovery logic.
    /// Null uses the global default from SchedulerConfiguration. Written to an existing manifest
    /// only when set (setting null states "use the global default"): left unset, a re-seed keeps
    /// the manifest's current value.
    /// </remarks>
    public TimeSpan? Timeout
    {
        get => _timeout;
        set
        {
            _timeout = value;
            _timeoutStated = true;
        }
    }

    internal TimeSpan? _timeout;
    internal bool _timeoutStated;

    /// <summary>
    /// Gets or sets the default dispatch priority for this manifest's work queue entries.
    /// </summary>
    /// <remarks>
    /// Range: 0 (lowest) to 31 (highest). Higher-priority entries are dispatched first
    /// by the JobDispatcher. For dependent manifests, a configurable boost is applied
    /// on top of this value (see <see cref="SchedulerConfiguration.DependentPriorityBoost"/>).
    /// A new manifest takes 0 when none is set. Written to an existing manifest only when set:
    /// left unset, a re-seed keeps the manifest's current value.
    /// </remarks>
    public int Priority
    {
        get => _priority ?? 0;
        set => _priority = value;
    }

    internal int? _priority;

    /// <summary>
    /// Gets or sets whether this dependent manifest is dormant.
    /// </summary>
    /// <remarks>
    /// Dormant dependents are declared in the fluent API like normal dependents but are
    /// never auto-fired when the parent succeeds. They must be explicitly activated at
    /// runtime by the parent train via <c>IDormantDependentContext</c>. Only meaningful
    /// for dependent manifests (created via Include/ThenInclude).
    /// </remarks>
    public bool IsDormant { get; set; }

    /// <summary>
    /// Gets or sets the per-manifest misfire policy override.
    /// Null means use the global default, <c>SchedulerConfiguration.DefaultMisfirePolicy</c>.
    /// </summary>
    public MisfirePolicy? MisfirePolicy { get; set; }

    /// <summary>
    /// Gets or sets the per-manifest misfire threshold override.
    /// Null means use the global default from SchedulerConfiguration.DefaultMisfireThreshold.
    /// </summary>
    public TimeSpan? MisfireThreshold { get; set; }

    /// <summary>
    /// Gets or sets the exclusion windows for this manifest.
    /// </summary>
    /// <remarks>
    /// When any exclusion matches the current time, the manifest is not scheduled.
    /// Excluded periods are treated as "intentionally skipped", not as misfires.
    /// Empty list means no exclusions.
    /// </remarks>
    public List<Exclusion> Exclusions { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum random delay added to each scheduled run.
    /// </summary>
    /// <remarks>
    /// When set, after each successful execution the scheduler adds a random delay
    /// of <c>[0, Variance]</c> to the next scheduled time. Only applies to Cron and
    /// Interval schedule types. Null means no variance (deterministic scheduling).
    /// </remarks>
    public TimeSpan? Variance { get; set; }

    /// <summary>
    /// Gets or sets how far back this manifest's failed runs count toward its retry backoff and
    /// its dead letter. Null means the scheduler's
    /// <see cref="SchedulerConfiguration.FailureCountWindow"/> applies.
    /// </summary>
    /// <remarks>
    /// A failure that started before the window no longer delays the next run or counts toward
    /// <see cref="MaxRetries"/>. Stored in whole seconds. Written to an existing manifest only
    /// when set: left null, a re-seed keeps the window the manifest already has.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value set is not between one second and ten years.
    /// </exception>
    public TimeSpan? FailureWindow
    {
        get => _failureWindow;
        set
        {
            if (value is { } window)
                ThrowIfFailureWindowOutOfRange(window);
            _failureWindow = value;
        }
    }

    private TimeSpan? _failureWindow;

    internal static void ThrowIfFailureWindowOutOfRange(TimeSpan window)
    {
        if (
            Services.Operations.SchedulerConfigLimits.PositiveDuration(
                window,
                nameof(FailureWindow)
            ) is
            { } problem
        )
            throw new ArgumentOutOfRangeException(nameof(window), window, problem);
    }

    /// <summary>
    /// A copy of these options with its own exclusion list, so a change to one item's options
    /// in a batch cannot reach another's. Copies every field, stated or not.
    /// </summary>
    internal ManifestOptions Copy() =>
        new()
        {
            _isEnabled = _isEnabled,
            _maxRetries = _maxRetries,
            _defaultMaxRetries = _defaultMaxRetries,
            _timeout = _timeout,
            _timeoutStated = _timeoutStated,
            _priority = _priority,
            IsDormant = IsDormant,
            MisfirePolicy = MisfirePolicy,
            MisfireThreshold = MisfireThreshold,
            Exclusions = [.. Exclusions],
            Variance = Variance,
            FailureWindow = FailureWindow,
        };
}
