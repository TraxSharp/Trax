using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Patch input for <see cref="IOperationsService.UpdateManifestAsync"/>. Every field is
/// independent: a <c>null</c> value leaves that field unchanged. Set <see cref="ClearTimeout"/>
/// to remove the per-execution timeout, since a <c>null</c> <see cref="TimeoutSeconds"/> means
/// "no change", not "clear".
/// </summary>
/// <param name="IsEnabled">Whether the scheduler runs the manifest.</param>
/// <param name="MaxRetries">How many times a failed run is retried; not negative.</param>
/// <param name="Priority">
/// The priority its work queue entries get, from <c>WorkQueue.MinPriority</c> to
/// <c>WorkQueue.MaxPriority</c>.
/// </param>
/// <param name="TimeoutSeconds">The per-execution timeout; greater than zero.</param>
/// <param name="ClearTimeout">When true, removes the timeout; <see cref="TimeoutSeconds"/> is ignored.</param>
/// <param name="ScheduleType">
/// The schedule kind. Only <c>None</c>, <c>Cron</c>, <c>Interval</c> and <c>OnDemand</c> can be
/// switched to here: <c>Once</c>, <c>Dependent</c> and <c>DormantDependent</c> need a time or a
/// parent this input cannot give.
/// </param>
/// <param name="CronExpression">A 5-field (or 6-field, with seconds) cron expression that fires.</param>
/// <param name="IntervalSeconds">The interval for an <c>Interval</c> schedule; greater than zero.</param>
public record ManifestUpdate(
    bool? IsEnabled = null,
    int? MaxRetries = null,
    int? Priority = null,
    int? TimeoutSeconds = null,
    bool ClearTimeout = false,
    ScheduleType? ScheduleType = null,
    string? CronExpression = null,
    int? IntervalSeconds = null
);
