namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for individual scheduled manifests.
/// </summary>
/// <remarks>
/// ManifestOptions provides fine-grained control over individual job behavior.
/// Default values are applied when scheduling a job, and can be overridden
/// via the configure action in <see cref="Services.ManifestScheduler.IManifestScheduler.ScheduleAsync"/>.
/// </remarks>
/// <example>
/// <code>
/// await scheduler.ScheduleAsync&lt;IMyWorkflow, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Every.Minutes(5),
///     opts =>
///     {
///         opts.IsEnabled = true;
///         opts.MaxRetries = 5;
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
    /// This allows pausing jobs without deleting them. Defaults to true.
    /// </remarks>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry attempts before dead-lettering.
    /// </summary>
    /// <remarks>
    /// Each retry creates a new Metadata record. After this many failed attempts,
    /// the job is moved to the dead letter queue for manual intervention.
    /// Defaults to 3.
    /// </remarks>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the timeout for job execution.
    /// </summary>
    /// <remarks>
    /// If a job is in "InProgress" state for longer than this duration,
    /// it may be considered stuck and subject to recovery logic.
    /// Null uses the global default from SchedulerConfiguration.
    /// </remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the default dispatch priority for this manifest's work queue entries.
    /// </summary>
    /// <remarks>
    /// Range: 0 (lowest) to 31 (highest). Higher-priority entries are dispatched first
    /// by the JobDispatcher. For dependent manifests, a configurable boost is applied
    /// on top of this value (see <see cref="SchedulerConfiguration.DependentPriorityBoost"/>).
    /// </remarks>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets whether this dependent manifest is dormant.
    /// </summary>
    /// <remarks>
    /// Dormant dependents are declared in the fluent API like normal dependents but are
    /// never auto-fired when the parent succeeds. They must be explicitly activated at
    /// runtime by the parent workflow via <c>IDormantDependentContext</c>. Only meaningful
    /// for dependent manifests (created via Include/ThenInclude).
    /// </remarks>
    public bool IsDormant { get; set; }
}
