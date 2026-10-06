namespace Trax.Scheduler.Services.TraxScheduler;

/// <summary>
/// What a manifest trigger did, returned by the <c>TriggerAsync</c> overloads that take
/// <c>askAfresh</c>.
/// </summary>
/// <param name="WorkQueueId">The work queue entry that runs the triggered run.</param>
/// <param name="Created">
/// True when the trigger queued a new entry; false when the manifest already had a queued entry
/// and the trigger released that one instead.
/// </param>
/// <param name="ScheduledAt">When the entry is due; null means immediately.</param>
/// <param name="AlreadyDispatched">
/// True when the manifest's queued entry left the queue (the dispatcher claimed it) between the
/// trigger finding it and changing it, so the trigger changed nothing about it: it was not brought
/// forward and, when the trigger asked afresh, its run replays the decisions it was queued to
/// replay anyway. <see cref="ReplayDecisionsOf"/> says whether it does.
/// </param>
/// <param name="ReplayDecisionsOf">
/// The run whose decisions the triggered run replays, as the trigger left the entry; null when it
/// asks its deciders afresh. A new entry never replays.
/// </param>
public record ManifestTriggerResult(
    long WorkQueueId,
    bool Created,
    DateTime? ScheduledAt,
    bool AlreadyDispatched,
    long? ReplayDecisionsOf
)
{
    /// <summary>
    /// True when the manifest already had a queued entry due later than the trigger asked, so the
    /// trigger brought that entry forward to <see cref="ScheduledAt"/>. False for a new entry, for
    /// an existing entry already due by then, and for one the dispatcher had claimed.
    /// </summary>
    public bool MovedForward { get; init; }
}
