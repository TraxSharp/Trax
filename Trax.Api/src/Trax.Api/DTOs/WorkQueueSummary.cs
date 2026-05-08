using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

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
    string? InputTypeName
);
