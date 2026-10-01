using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The persisted-operation management surface: reads and writes with the same checks, refusals
/// and error codes wherever they are called from. The GraphQL <c>operations.persistedOperations</c>
/// fields call it, and a dashboard or admin tool calls it to behave exactly as the API does.
/// </summary>
/// <remarks>
/// Writes never throw for a refused change: the payload carries <c>success: false</c> and errors
/// with a stable <c>code</c> (<c>INVALID_INPUT</c>, <c>NOT_FOUND</c>, <c>PARSE_FAILED</c>,
/// <c>SCHEMA_VALIDATION_FAILED</c>, <c>SHAPE_DIFF_VIOLATION</c>). Registered by
/// <c>UsePersistedOperations</c> and <c>AddPersistedOperationStore</c>.
/// </remarks>
public interface IPersistedOperationsService
{
    /// <summary>
    /// A page of operations, most recently updated first. <paramref name="take"/> outside 1 to
    /// 200 reads as 50; a negative <paramref name="skip"/> reads as 0.
    /// </summary>
    Task<PersistedOperationsPage> ListAsync(
        PersistedOperationFilter? filter,
        int skip,
        int take,
        CancellationToken ct
    );

    /// <summary>One operation, active or deactivated, or null when there is none.</summary>
    Task<PersistedOperationDto?> GetAsync(string id, string? tenantKey, CancellationToken ct);

    /// <summary>
    /// An operation's change history, most recent first. Paging is clamped as in
    /// <see cref="ListAsync"/>.
    /// </summary>
    Task<IReadOnlyList<PersistedOperationHistoryDto>> GetHistoryAsync(
        string id,
        string? tenantKey,
        int skip,
        int take,
        CancellationToken ct
    );

    /// <summary>
    /// Inserts or updates an operation. The document is validated against the live schema, must
    /// hold exactly one operation, and may not change the response shape of an existing id unless
    /// <c>BypassShapeDiff</c> is set.
    /// </summary>
    Task<UploadPersistedOperationPayload> UploadAsync(
        UploadPersistedOperationInput input,
        CancellationToken ct
    );

    /// <summary>Deactivates an active operation. A reason is required.</summary>
    Task<DeactivatePersistedOperationPayload> DeactivateAsync(
        DeactivatePersistedOperationInput input,
        CancellationToken ct
    );

    /// <summary>Reactivates an operation, active or deactivated.</summary>
    Task<RestorePersistedOperationPayload> RestoreAsync(
        RestorePersistedOperationInput input,
        CancellationToken ct
    );
}
