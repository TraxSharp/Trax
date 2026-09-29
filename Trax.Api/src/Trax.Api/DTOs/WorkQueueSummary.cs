using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// A work queue entry as the list reads return it: a request to run a train that waits to be
/// dispatched. Carries no input; the single-entry detail read returns <see cref="WorkQueueDetail"/>
/// with it.
/// </summary>
/// <param name="Id">The entry's id.</param>
/// <param name="ExternalId">The entry's stable external id.</param>
/// <param name="TrainName">The name of the train the entry runs.</param>
/// <param name="Status">The entry's status.</param>
/// <param name="CreatedAt">When the entry was queued (UTC).</param>
/// <param name="DispatchedAt">When the entry was dispatched (UTC), or <c>null</c> while it is queued.</param>
/// <param name="ScheduledAt">The earliest time the entry may be dispatched (UTC), for a delayed entry.</param>
/// <param name="Priority">The entry's dispatch priority.</param>
/// <param name="DispatchAttempts">How many times dispatch has been attempted.</param>
/// <param name="ManifestId">The manifest that queued the entry, if any.</param>
/// <param name="MetadataId">The execution the entry started, once dispatched.</param>
/// <param name="DeadLetterId">The dead letter the entry was requeued from, if any.</param>
/// <param name="InputTypeName">The full name of the train input type.</param>
/// <param name="ConfirmedAt">When the entry became eligible for dispatch (UTC), or <c>null</c> while it is still being staged.</param>
/// <param name="SubjectKey">The subject the entry serializes on, or <c>null</c> for none.</param>
public record WorkQueueSummary(
    long Id,
    string ExternalId,
    string TrainName,
    WorkQueueStatus Status,
    DateTime CreatedAt,
    DateTime? DispatchedAt,
    DateTime? ScheduledAt,
    int Priority,
    int DispatchAttempts,
    long? ManifestId,
    long? MetadataId,
    long? DeadLetterId,
    string? InputTypeName,
    DateTime? ConfirmedAt = null,
    string? SubjectKey = null
);
