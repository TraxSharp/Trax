using Trax.Effect.Models.PersistedOperation;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Programmatic CRUD against <c>trax.persisted_operation</c>. Surface for
/// admin tooling, manifest uploaders, and tests.
/// </summary>
/// <remarks>
/// The HTTP request path does NOT use this interface; HotChocolate calls
/// <see cref="HotChocolate.PersistedOperations.IOperationDocumentStorage.TryReadAsync"/>
/// (implemented internally by the package) instead. Use this interface for
/// administrative changes only.
/// </remarks>
public interface IPersistedOperationStore
{
    /// <summary>
    /// Fetch a single active operation by id, or null if missing or deactivated.
    /// </summary>
    Task<PersistedOperation?> GetAsync(string id, string? tenantKey, CancellationToken ct);

    /// <summary>
    /// List all operations for the tenant (active + deactivated).
    /// </summary>
    Task<IReadOnlyList<PersistedOperation>> ListAsync(string? tenantKey, CancellationToken ct);

    /// <summary>
    /// Insert or update an operation. Computes the shape fingerprint, parses
    /// the version from the id suffix, and writes a history row alongside
    /// the live row. Invalidates the local cache and publishes a broadcast
    /// invalidation if either is configured.
    /// </summary>
    /// <exception cref="ArgumentException">When id or document are empty.</exception>
    /// <exception cref="FormatException">When the id is not in the form <c>name.vN</c>.</exception>
    Task<PersistedOperation> UpsertAsync(
        string id,
        string document,
        UpsertOptions? options,
        CancellationToken ct
    );

    /// <summary>
    /// Soft-delete an operation. Subsequent requests for the id resolve to
    /// null; clients receive a typed not-found error. Reason is required
    /// for audit history.
    /// </summary>
    Task DeactivateAsync(string id, string? tenantKey, string reason, CancellationToken ct);

    /// <summary>
    /// Reactivate a previously deactivated operation.
    /// </summary>
    Task RestoreAsync(string id, string? tenantKey, CancellationToken ct);
}
