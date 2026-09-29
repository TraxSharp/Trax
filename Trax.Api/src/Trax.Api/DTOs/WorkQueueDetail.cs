using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// Full detail for a single work queue entry: everything <see cref="WorkQueueSummary"/> carries,
/// the train input it was queued with, and what a queued entry with a subject is waiting on.
/// </summary>
/// <remarks>
/// <see cref="Input"/> can hold anything the train takes, credentials included. It follows the
/// rule an execution's input follows: it appears on the single-row detail read only, never on a
/// list, and only under the <c>operations</c> namespace, so the host's operations gate decides
/// who reads it.
/// </remarks>
/// <param name="Id">The entry's database id.</param>
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
/// <param name="Input">The serialized train input.</param>
/// <param name="SubjectHeldBy">
/// For a queued entry with a subject: the dispatched entry for the same subject whose run is
/// still pending or in progress. Dispatch skips the subject until that run finishes.
/// </param>
/// <param name="SubjectQueuedBehind">
/// For a queued entry with a subject that nothing is holding: the queued entry for the same
/// subject that dispatch would offer first. Dispatch offers one entry per subject each cycle.
/// </param>
public record WorkQueueDetail(
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
    DateTime? ConfirmedAt,
    string? SubjectKey,
    string? Input,
    long? SubjectHeldBy,
    long? SubjectQueuedBehind
);
