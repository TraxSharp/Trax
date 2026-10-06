using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// What <see cref="IOperationsService.TriggerManifestAsync"/> did with one manifest, for an
/// operator: the dashboard's Trigger buttons and the API's <c>triggerManifest</c> and
/// <c>triggerManifestDelayed</c> show <see cref="Message"/>.
/// </summary>
/// <param name="Success">
/// False when nothing was triggered: no manifest has the external id, or nothing on this host can
/// dispatch a queued run. <see cref="Trigger"/> is then <c>null</c>.
/// </param>
/// <param name="Message">
/// One line saying which happened: a new entry was queued, the manifest's queued entry was brought
/// forward, its queued entry already ran at or before the asked time and now runs as the trigger,
/// or the dispatcher had already claimed it (and, when the trigger asked afresh, that the run still
/// replays). Always set.
/// </param>
/// <param name="Trigger">The trigger's detail when it succeeded; <c>null</c> otherwise.</param>
public record TriggerManifestResult(bool Success, string Message, ManifestTriggerResult? Trigger)
{
    /// <summary>
    /// True when the trigger asked afresh but the dispatcher had already claimed the manifest's
    /// queued retry, so its run replays the decisions of <see cref="ManifestTriggerResult.ReplayDecisionsOf"/>
    /// anyway. The trigger still succeeded.
    /// </summary>
    public bool StillReplaying { get; init; }
}
