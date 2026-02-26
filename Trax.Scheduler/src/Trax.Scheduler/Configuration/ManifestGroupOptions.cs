namespace Trax.Scheduler.Configuration;

/// <summary>
/// Fluent builder for group-level dispatch settings.
/// Used within <see cref="ScheduleOptions.Group(System.Action{ManifestGroupOptions})"/> to configure
/// a <see cref="Trax.Effect.Models.ManifestGroup.ManifestGroup"/>.
/// </summary>
/// <remarks>
/// Nullable internal fields distinguish "not set" (inherit defaults) from "explicitly set to a value".
/// When a field is not set, the scheduler applies a sensible default:
/// <list type="bullet">
///   <item><c>MaxActiveJobs</c>: null (no per-group limit; only the global limit applies)</item>
///   <item><c>Priority</c>: inherits from the manifest-level priority</item>
///   <item><c>IsEnabled</c>: true</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// scheduler.Schedule&lt;IMyWorkflow, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Every.Minutes(5),
///     options => options.Group(group => group
///         .MaxActiveJobs(5)
///         .Priority(20)
///         .Enabled(true)));
/// </code>
/// </example>
public class ManifestGroupOptions
{
    internal int? _maxActiveJobs;
    internal int? _priority;
    internal bool? _isEnabled;

    /// <summary>
    /// Sets the maximum number of concurrent active jobs for this group.
    /// Null means no per-group limit (only the global MaxActiveJobs applies).
    /// </summary>
    public ManifestGroupOptions MaxActiveJobs(int? max)
    {
        _maxActiveJobs = max;
        return this;
    }

    /// <summary>
    /// Sets the dispatch priority for this group (0-31).
    /// Higher-priority groups have their work queue entries dispatched first.
    /// </summary>
    public ManifestGroupOptions Priority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Sets whether manifests in this group are eligible for dispatch.
    /// When false, no manifests in this group will be queued or dispatched.
    /// </summary>
    public ManifestGroupOptions Enabled(bool enabled)
    {
        _isEnabled = enabled;
        return this;
    }
}
