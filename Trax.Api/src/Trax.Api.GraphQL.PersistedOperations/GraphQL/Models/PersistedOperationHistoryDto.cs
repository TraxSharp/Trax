namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>One row from the operation's audit log.</summary>
public sealed record PersistedOperationHistoryDto(
    long HistoryId,
    string Id,
    string? TenantKey,
    string Document,
    string ShapeFingerprint,
    string ChangeType,
    DateTime ChangedAt,
    string? ChangedReason
);
