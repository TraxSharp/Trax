using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One work queue entry as an operator reads it: its fields, its train input with every
/// <c>[TraxSensitive]</c> member masked, and for a queued entry with a subject, what it is waiting
/// on. Returned by <see cref="IOperationsService.GetWorkQueueEntryDetailAsync"/>, which backs both
/// the dashboard's work queue entry page and the GraphQL API's <c>workQueue.detail</c>.
/// </summary>
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
/// <param name="Input">
/// The entry's train input as <see cref="TransportInputRedaction.Redact"/> masks it, or
/// <c>null</c> when it has none. The stored copy keeps its sensitive members in clear because the
/// run reads it; this never does.
/// </param>
/// <param name="SubjectHeldBy">
/// For a queued entry with a subject: the dispatched entry for the same subject whose run is
/// still pending or in progress. Dispatch skips the subject until that run finishes.
/// </param>
/// <param name="SubjectQueuedBehind">
/// For a queued entry with a subject that nothing is holding: the queued entry for the same
/// subject that dispatch would offer first. Dispatch offers one entry per subject each cycle.
/// </param>
/// <param name="ReplayDecisionsOf">
/// The execution whose recorded decisions the run this entry starts will replay, or <c>null</c>
/// when it will ask its questions afresh.
/// </param>
public record WorkQueueEntryDetail(
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
    long? SubjectQueuedBehind,
    long? ReplayDecisionsOf
);
