using Trax.Effect.Models.PersistedOperation;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>
/// GraphQL surface for a persisted operation row.
/// </summary>
public sealed record PersistedOperationDto(
    string Id,
    string? TenantKey,
    string OperationName,
    int Version,
    string Document,
    string ShapeFingerprint,
    bool IsActive,
    string? DeprecationReason,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    internal static PersistedOperationDto From(PersistedOperation row) =>
        new(
            row.Id,
            row.TenantKey,
            row.OperationName,
            row.Version,
            row.Document,
            row.ShapeFingerprint,
            row.IsActive,
            row.DeprecationReason,
            row.Description,
            row.CreatedAt,
            row.UpdatedAt
        );
}
