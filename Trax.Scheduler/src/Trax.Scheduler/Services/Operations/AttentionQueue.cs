namespace Trax.Scheduler.Services.Operations;

/// <summary>The queued work that is due and has not been dispatched.</summary>
/// <param name="Queued">
/// How many entries are queued, confirmed and due (no scheduled time, or one that has passed): what
/// the dispatcher would offer to a worker now.
/// </param>
/// <param name="OldestQueuedAt">When the oldest of them was queued (UTC); null when none is.</param>
/// <param name="OldestAgeSeconds">How long the oldest has waited, in seconds; null when none is queued.</param>
/// <param name="Link">The console's queue, filtered to queued entries.</param>
public sealed record AttentionQueue(
    long Queued,
    DateTime? OldestQueuedAt,
    double? OldestAgeSeconds,
    string Link
);
