using System.ComponentModel;
using HotChocolate;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL;

/// <summary>
/// Body of the <c>operations.persistedOperations</c> mutation namespace. Each mutation calls
/// <see cref="IPersistedOperationsService"/>, which returns refusals as payload <c>errors[]</c>
/// entries with stable <c>code</c> values; mutations never throw to the client.
/// </summary>
/// <remarks>
/// The overloads taking <see cref="IPersistedOperationStore"/> are kept for callers built against
/// them and are not part of the schema. New code calls <see cref="IPersistedOperationsService"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PersistedOperationMutations
{
    /// <summary>
    /// Upload (insert or update) a persisted operation. Validates the document against the live
    /// schema, requires exactly one operation, and rejects shape-changing edits unless
    /// <c>bypassShapeDiff</c> is set.
    /// </summary>
    public Task<UploadPersistedOperationPayload> UploadPersistedOperation(
        UploadPersistedOperationInput input,
        [Service] IPersistedOperationsService service,
        CancellationToken ct
    ) => service.UploadAsync(input, ct);

    /// <summary>Soft-delete an operation. Requires a non-empty reason.</summary>
    public Task<DeactivatePersistedOperationPayload> DeactivatePersistedOperation(
        DeactivatePersistedOperationInput input,
        [Service] IPersistedOperationsService service,
        CancellationToken ct
    ) => service.DeactivateAsync(input, ct);

    /// <summary>Reactivate a previously deactivated operation.</summary>
    public Task<RestorePersistedOperationPayload> RestorePersistedOperation(
        RestorePersistedOperationInput input,
        [Service] IPersistedOperationsService service,
        CancellationToken ct
    ) => service.RestoreAsync(input, ct);

    /// <summary>Kept for callers built against it; calls <see cref="IPersistedOperationsService.UploadAsync"/>.</summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<UploadPersistedOperationPayload> UploadPersistedOperation(
        UploadPersistedOperationInput input,
        IPersistedOperationStore store,
        CancellationToken ct
    ) => PersistedOperationsService.ForStore(store).UploadAsync(input, ct);

    /// <summary>Kept for callers built against it; calls <see cref="IPersistedOperationsService.DeactivateAsync"/>.</summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<DeactivatePersistedOperationPayload> DeactivatePersistedOperation(
        DeactivatePersistedOperationInput input,
        IPersistedOperationStore store,
        CancellationToken ct
    ) => PersistedOperationsService.ForStore(store).DeactivateAsync(input, ct);

    /// <summary>Kept for callers built against it; calls <see cref="IPersistedOperationsService.RestoreAsync"/>.</summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<RestorePersistedOperationPayload> RestorePersistedOperation(
        RestorePersistedOperationInput input,
        IPersistedOperationStore store,
        IDataContextProviderFactory contextFactory,
        CancellationToken ct
    ) => new PersistedOperationsService(store, contextFactory).RestoreAsync(input, ct);
}
