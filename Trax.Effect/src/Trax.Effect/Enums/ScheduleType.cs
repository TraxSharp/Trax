namespace Trax.Effect.Enums;

/// <summary>
/// Defines how a manifest should be scheduled for execution.
/// </summary>
/// <remarks>
/// The ScheduleType enum determines the scheduling behavior of a manifest.
/// Different schedule types are suited for different use cases:
///
/// - <see cref="None"/> for manual-only jobs that should never auto-run
/// - <see cref="Cron"/> for traditional cron-based scheduling (e.g., "every day at 3am")
/// - <see cref="Interval"/> for simple recurring intervals (e.g., "every 5 minutes")
/// - <see cref="OnDemand"/> for bulk operations triggered programmatically
/// </remarks>
public enum ScheduleType
{
    /// <summary>
    /// The manifest can only be triggered manually via API or code.
    /// </summary>
    /// <remarks>
    /// Use this for jobs that should never run automatically.
    /// They must be explicitly triggered using <c>ManifestManager.TriggerManifestAsync()</c>.
    /// </remarks>
    None = 0,

    /// <summary>
    /// The manifest runs on a schedule defined by a cron expression.
    /// </summary>
    /// <remarks>
    /// Standard cron expressions are supported (e.g., "0 3 * * *" for daily at 3am).
    /// The <see cref="Manifest.Manifest.CronExpression"/> property must be set when using this type.
    /// </remarks>
    Cron = 1,

    /// <summary>
    /// The manifest runs at a fixed interval.
    /// </summary>
    /// <remarks>
    /// Use this for simple recurring jobs where cron expressions are overkill.
    /// The <see cref="Manifest.Manifest.IntervalSeconds"/> property must be set when using this type.
    /// </remarks>
    Interval = 2,

    /// <summary>
    /// The manifest is designed for bulk/batch operations triggered programmatically.
    /// </summary>
    /// <remarks>
    /// Use this for scenarios like database replication where you need to enqueue
    /// many jobs at once (e.g., one per table slice). These jobs are not automatically
    /// scheduled by the ManifestManager polling loop, but are instead triggered via
    /// <c>ManifestManager.BulkEnqueueAsync()</c>.
    ///
    /// This type signals intent that the manifest is designed for bulk operations
    /// and helps with monitoring and reporting.
    /// </remarks>
    OnDemand = 3,

    /// <summary>
    /// The manifest runs after a parent manifest completes successfully.
    /// </summary>
    /// <remarks>
    /// Use this for trains that should be triggered by the successful completion of
    /// another train. The <see cref="Manifest.Manifest.DependsOnManifestId"/> property
    /// must be set to the parent manifest's ID. The dependent manifest is queued when the
    /// parent's <see cref="Manifest.Manifest.LastSuccessfulRun"/> is newer than the
    /// dependent's own <see cref="Manifest.Manifest.LastSuccessfulRun"/>.
    /// </remarks>
    Dependent = 4,

    /// <summary>
    /// The manifest is declared as a dependent but only fires when explicitly activated
    /// at runtime by the parent train via <c>IDormantDependentContext</c>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Dependent"/>, dormant dependents are never automatically queued
    /// when the parent succeeds. The parent train must explicitly call
    /// <c>IDormantDependentContext.ActivateAsync</c> with runtime-determined input.
    /// The <see cref="Manifest.Manifest.DependsOnManifestId"/> property must be set
    /// to the parent manifest's ID. Use this when the parent needs to decide at runtime
    /// which dependents fire and with what input.
    /// </remarks>
    DormantDependent = 5,
}
