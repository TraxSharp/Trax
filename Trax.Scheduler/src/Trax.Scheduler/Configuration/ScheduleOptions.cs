namespace Trax.Scheduler.Configuration;

/// <summary>
/// Unified fluent builder for all scheduling options — manifest-level, group-level, and batch-level.
/// Replaces the separate <c>configure</c>, <c>groupId</c>, <c>priority</c>, and <c>prunePrefix</c>
/// optional parameters with a single <c>Action&lt;ScheduleOptions&gt;</c> callback.
/// </summary>
/// <example>
/// <code>
/// scheduler.Schedule&lt;IMyWorkflow, MyInput&gt;(
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
    internal int _priority;
    internal bool _isEnabled = true;
    internal int _maxRetries = 3;
    internal TimeSpan? _timeout;
    internal bool _isDormant;

    // Group-level state
    internal string? _groupId;
    internal ManifestGroupOptions? _groupOptions;

    // Batch-level state
    internal string? _prunePrefix;

    // ── Manifest-level fluent methods ─────────────────────────────────

    /// <summary>
    /// Sets the dispatch priority for this manifest (0-31).
    /// Higher values are dispatched first.
    /// </summary>
    public ScheduleOptions Priority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Sets whether this manifest is enabled for scheduling.
    /// </summary>
    public ScheduleOptions Enabled(bool enabled)
    {
        _isEnabled = enabled;
        return this;
    }

    /// <summary>
    /// Sets the maximum retry attempts before dead-lettering.
    /// </summary>
    public ScheduleOptions MaxRetries(int retries)
    {
        _maxRetries = retries;
        return this;
    }

    /// <summary>
    /// Sets the timeout for job execution.
    /// </summary>
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
    /// prefix but were not in the current batch will be deleted.
    /// </summary>
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
            Priority = _priority,
            IsEnabled = _isEnabled,
            MaxRetries = _maxRetries,
            Timeout = _timeout,
            IsDormant = _isDormant,
        };
}
