namespace Trax.Effect.Enums;

/// <summary>
/// Represents the lifecycle status of a work queue entry.
/// </summary>
public enum WorkQueueStatus
{
    /// <summary>
    /// The entry is waiting to be picked up by the JobDispatcher.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// The entry has been dispatched — a Metadata record was created and the job was enqueued.
    /// </summary>
    Dispatched = 1,

    /// <summary>
    /// The entry was cancelled before dispatch.
    /// </summary>
    Cancelled = 2,
}
