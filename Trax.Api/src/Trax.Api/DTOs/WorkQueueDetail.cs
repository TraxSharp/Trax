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
