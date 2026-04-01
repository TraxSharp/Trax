using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

public record DeadLetterSummary(
    long Id,
    long ManifestId,
    string ManifestName,
    DeadLetterStatus Status,
    DateTime DeadLetteredAt,
    string Reason,
    int RetryCountAtDeadLetter,
    DateTime? ResolvedAt,
    string? ResolutionNote,
    long? RetryMetadataId
);
