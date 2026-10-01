using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Unified fluent builder for all scheduling options — manifest-level, group-level, and batch-level.
/// Replaces the separate <c>configure</c>, <c>groupId</c>, <c>priority</c>, and <c>prunePrefix</c>
/// optional parameters with a single <c>Action&lt;ScheduleOptions&gt;</c> callback.
/// </summary>
/// <remarks>
/// Every host start schedules its manifests again. The schedule, the input and the train always
/// come from code. <see cref="Enabled"/>, <see cref="MaxRetries"/>, <see cref="Timeout"/>,
/// <see cref="Priority"/> and the group settings are written only when the options state them, so
/// a manifest or group an operator disabled or retuned at runtime keeps that state across restarts
/// unless the code says otherwise. A new manifest takes the scheduler's <c>DefaultMaxRetries</c>
/// when <see cref="MaxRetries"/> is not stated, and <see cref="OnMisfire"/> falls back to
/// <c>DefaultMisfirePolicy</c>.
/// </remarks>
/// <example>
/// <code>
/// scheduler.Schedule&lt;IMyTrain&gt;(
///     "my-job",
///     new MyInput(),
///     Every.Minutes(5),
///     options => options
///         .Priority(10)
///         .MaxRetries(5)
///         .Group("my-group", group => group
///             .MaxActiveJobs(5)
///             .Priority(20)));
/// </code>
/// </example>
public class ScheduleOptions
{
    // Manifest-level state
    // Nullable where "not stated" differs from any value: see the class remarks.
    internal int? _priority;
    internal bool? _isEnabled;
    internal int? _maxRetries;
    internal TimeSpan? _timeout;
    internal bool _isDormant;
    internal MisfirePolicy? _misfirePolicy;
    internal TimeSpan? _misfireThreshold;
    internal List<Exclusion> _exclusions = [];
    internal TimeSpan? _variance;
    internal TimeSpan? _failureWindow;

    // Group-level state
    internal string? _groupId;
    internal ManifestGroupOptions? _groupOptions;

    // Batch-level state
    internal string? _prunePrefix;

    // Set by the named ScheduleMany/IncludeMany/ThenIncludeMany overloads: the batch's prune is
    // scoped to the batch's own group rather than to every manifest sharing its prefix.
    internal string? _batchName;

    // ── Manifest-level fluent methods ─────────────────────────────────

    /// <summary>
    /// Sets the dispatch priority for this manifest (0-31).
    /// Higher values are dispatched first.
    /// </summary>
    /// <remarks>
    /// Stated, it is written on every seed. Left unstated, a new manifest takes 0 and an existing
    /// one keeps whatever it is, including a runtime change.
    /// </remarks>
    public ScheduleOptions Priority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Sets whether this manifest is enabled for scheduling.
    /// </summary>
    /// <remarks>
    /// Stated, it is written on every seed. Left unstated, a new manifest is enabled and an existing
    /// one keeps whatever it is, including a runtime disable.
    /// </remarks>
    public ScheduleOptions Enabled(bool enabled)
    {
        _isEnabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets how many times a failed run is retried before the manifest is dead-lettered. Unstated,
    /// a new manifest takes the scheduler's <c>DefaultMaxRetries</c> and an existing one keeps
    /// whatever it is, including a runtime change.
    /// </summary>
    /// <remarks>
    /// The count is of retries after the first run, so a manifest runs at most
    /// <paramref name="retries"/> + 1 times in a row before it is dead-lettered: <c>MaxRetries(0)</c>
    /// runs once and dead-letters on the first failure, and the default of 3 allows four attempts.
    /// Failures count within the manifest's <see cref="FailureWindow"/> (the scheduler's
    /// <see cref="SchedulerConfiguration.FailureCountWindow"/> when unstated) and after the
    /// manifest's latest resolved dead letter.
    /// </remarks>
    /// <param name="retries">The number of retries after the first run (default: 3).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retries"/> is negative.</exception>
    public ScheduleOptions MaxRetries(int retries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        _maxRetries = retries;
        return this;
    }

    /// <summary>
    /// Sets the timeout for job execution.
    /// </summary>
    /// <remarks>
    /// Stated, it is written on every seed. Left unstated, a new manifest has no timeout of its
    /// own and an existing one keeps whatever it is, including a runtime change.
    /// </remarks>
    public ScheduleOptions Timeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Marks this dependent manifest as dormant. Dormant dependents are never auto-fired
    /// when the parent succeeds; they must be explicitly activated at runtime via
    /// <see cref="Services.DormantDependentContext.IDormantDependentContext"/>.
    /// </summary>
    /// <remarks>
    /// Only meaningful for dependent manifests created via Include/IncludeMany/ThenInclude.
    /// The manifest is still registered in the topology (groups, DAG, dashboard) but the
    /// ManifestManager will not create WorkQueue entries for it on parent success.
    /// </remarks>
    public ScheduleOptions Dormant()
    {
        _isDormant = true;
        return this;
    }

    /// <summary>
    /// Sets the misfire policy for this manifest.
    /// </summary>
    /// <remarks>
    /// Determines behavior when a scheduled run is missed (e.g., scheduler was down).
    /// Only meaningful for Cron and Interval schedule types. Unstated, the manifest takes the
    /// scheduler's <c>DefaultMisfirePolicy</c>.
    /// </remarks>
    public ScheduleOptions OnMisfire(MisfirePolicy policy)
    {
        _misfirePolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets the misfire threshold for this manifest — the grace period before the misfire
    /// policy takes effect.
    /// </summary>
    /// <remarks>
    /// If a manifest is overdue by less than this threshold, it fires normally regardless of
    /// the misfire policy. Overrides the global DefaultMisfireThreshold.
    /// </remarks>
    public ScheduleOptions MisfireThreshold(TimeSpan threshold)
    {
        _misfireThreshold = threshold;
        return this;
    }

    /// <summary>
    /// Adds an exclusion window to this manifest. The manifest will not be scheduled
    /// during any period matched by the exclusion.
    /// </summary>
    /// <remarks>
    /// Multiple exclusions can be added. If ANY exclusion matches the current time,
    /// the manifest is skipped. Excluded periods are treated as "intentionally skipped"
    /// — not as misfires.
    /// </remarks>
    public ScheduleOptions Exclude(Exclusion exclusion)
    {
        _exclusions.Add(exclusion);
        return this;
    }

    /// <summary>
    /// Sets the maximum random delay added to each scheduled run (jitter).
    /// </summary>
    /// <remarks>
    /// After each successful execution, the scheduler adds a random delay of
    /// <c>[0, variance]</c> to the next scheduled time. This prevents thundering-herd
    /// problems and makes scheduling patterns less predictable. Only meaningful for
    /// Cron and Interval schedule types.
    /// </remarks>
    public ScheduleOptions Variance(TimeSpan variance)
    {
        _variance = variance;
        return this;
    }

    /// <summary>
    /// Sets how far back this manifest's failed runs count toward its retry backoff and its
    /// dead letter, in place of the scheduler's <see cref="SchedulerConfiguration.FailureCountWindow"/>.
    /// </summary>
    /// <remarks>
    /// A failure that started before the window no longer delays the next run or counts toward
    /// <see cref="MaxRetries"/>. Use a short window for a frequent job whose old failures say
    /// nothing about its health, and a long one for a daily job that should still dead-letter
    /// after failing on several days in a row. The window is stored in whole seconds.
    /// <para>
    /// Stated, it is written on every seed. Left unstated, a new manifest uses the scheduler's
    /// window and an existing one keeps the window it has.
    /// </para>
    /// </remarks>
    /// <param name="window">How far back failures count.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="window"/> is not between one second and ten years.
    /// </exception>
    public ScheduleOptions FailureWindow(TimeSpan window)
    {
        ManifestOptions.ThrowIfFailureWindowOutOfRange(window);
        _failureWindow = window;
        return this;
    }

    // ── Group-level fluent methods ────────────────────────────────────

    /// <summary>
    /// Configures the manifest group's dispatch settings.
    /// </summary>
    /// <param name="configure">Callback to configure group-level options like MaxActiveJobs and Priority.</param>
    public ScheduleOptions Group(Action<ManifestGroupOptions> configure)
    {
        _groupOptions ??= new ManifestGroupOptions();
        configure(_groupOptions);
        return this;
    }

    /// <summary>
    /// Sets the group name and optionally configures group-level dispatch settings.
    /// </summary>
    /// <param name="groupId">The manifest group name. All manifests with the same groupId share per-group dispatch controls.</param>
    /// <param name="configure">Optional callback to configure group-level options.</param>
    public ScheduleOptions Group(string groupId, Action<ManifestGroupOptions>? configure = null)
    {
        _groupId = groupId;
        if (configure is not null)
        {
            _groupOptions ??= new ManifestGroupOptions();
            configure(_groupOptions);
        }
        return this;
    }

    // ── Batch-level fluent methods ────────────────────────────────────

    /// <summary>
    /// Sets the prune prefix for batch scheduling. Manifests whose ExternalId starts with this
    /// prefix but were not in the current batch will be deleted, with their finished runs; a
    /// manifest with a pending or running run is kept until a later prune.
    /// </summary>
    /// <remarks>
    /// The named <c>ScheduleMany(name, ...)</c> overloads set this to <c>"{name}-"</c> and prune
    /// only within the batch's own group. Set directly, the prefix alone decides, so it also
    /// reaches manifests of another batch whose prefix starts with it; <c>AddScheduler</c> refuses
    /// two batches declared in the builder whose prunes overlap that way.
    /// </remarks>
    public ScheduleOptions PrunePrefix(string prefix)
    {
        _prunePrefix = prefix;
        return this;
    }

    // ── Internal helpers ──────────────────────────────────────────────

    /// <summary>
    /// Converts the manifest-level settings into a <see cref="ManifestOptions"/> instance.
    /// </summary>
    internal ManifestOptions ToManifestOptions() =>
        new()
        {
            _priority = _priority,
            _isEnabled = _isEnabled,
            _maxRetries = _maxRetries,
            _timeout = _timeout,
            _timeoutStated = _timeout is not null,
            IsDormant = _isDormant,
            MisfirePolicy = _misfirePolicy,
            MisfireThreshold = _misfireThreshold,
            Exclusions = [.. _exclusions],
            Variance = _variance,
            FailureWindow = _failureWindow,
        };

    /// <summary>
    /// Makes this a named batch: its manifests share the group <paramref name="name"/>, their
    /// external IDs start with <c>"{name}-"</c>, and its prune removes only manifests of its own
    /// group.
    /// </summary>
    internal ScheduleOptions NamedBatch(string name)
    {
        _groupId = name;
        _prunePrefix = $"{name}-";
        _batchName = name;
        return this;
    }
}
