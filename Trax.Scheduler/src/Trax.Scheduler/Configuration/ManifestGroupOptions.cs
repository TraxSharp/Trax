namespace Trax.Scheduler.Configuration;

/// <summary>
/// Fluent builder for group-level dispatch settings.
/// Used within <see cref="ScheduleOptions.Group(System.Action{ManifestGroupOptions})"/> to configure
/// a <see cref="Trax.Effect.Models.ManifestGroup.ManifestGroup"/>.
/// </summary>
/// <remarks>
/// A group setting is written only when a member states it. Seeding a manifest again leaves every
/// setting its options do not state as it is in the database, so a change an operator made at
/// runtime survives a restart. Two members that state different values for one setting fail the
/// build. When no member states a setting, a new group takes these values:
/// <list type="bullet">
///   <item><c>MaxActiveJobs</c>: null (no per-group limit; only the global limit applies)</item>
///   <item><c>Priority</c>: the manifest's priority</item>
///   <item><c>IsEnabled</c>: true</item>
/// </list>
/// A manifest scheduled without a group name has a group of its own, and a batch without one has
/// the batch's group; there the manifest's stated <c>Priority</c> is also the group's.
/// </remarks>
/// <example>
/// <code>
/// scheduler.Schedule&lt;IMyTrain&gt;(
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
    internal bool _maxActiveJobsStated;
    internal int? _priority;
    internal bool? _isEnabled;

    /// <summary>
    /// Sets the maximum number of concurrent active jobs for this group.
    /// Null means no per-group limit (only the global MaxActiveJobs applies).
    /// </summary>
    public ManifestGroupOptions MaxActiveJobs(int? max)
    {
        _maxActiveJobs = max;
        _maxActiveJobsStated = true;
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
