using System.ComponentModel;
using HotChocolate;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL;

/// <summary>
/// Body of the <c>operations.persistedOperations</c> query namespace. Each field calls
/// <see cref="IPersistedOperationsService"/>.
/// </summary>
/// <remarks>
/// The overloads taking <see cref="IDataContextProviderFactory"/> are kept for callers built
/// against them and are not part of the schema. New code calls
/// <see cref="IPersistedOperationsService"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PersistedOperationQueries
{
    /// <summary>List persisted operations, most recently updated first, paginated.</summary>
    public Task<PersistedOperationsPage> PersistedOperations(
        [Service] IPersistedOperationsService service,
        CancellationToken ct,
        PersistedOperationFilter? filter = null,
        int skip = 0,
        int take = 50
    ) => service.ListAsync(filter, skip, take, ct);

    /// <summary>Look up a single persisted operation. Returns null when missing.</summary>
    public Task<PersistedOperationDto?> PersistedOperation(
        string id,
        [Service] IPersistedOperationsService service,
        CancellationToken ct,
        string? tenantKey = null
    ) => service.GetAsync(id, tenantKey, ct);

    /// <summary>Audit history for an operation, most-recent-first.</summary>
    public Task<IReadOnlyList<PersistedOperationHistoryDto>> PersistedOperationHistory(
        string id,
        [Service] IPersistedOperationsService service,
        CancellationToken ct,
        string? tenantKey = null,
        int skip = 0,
        int take = 50
    ) => service.GetHistoryAsync(id, tenantKey, skip, take, ct);

    /// <summary>Kept for callers built against it; calls <see cref="IPersistedOperationsService.ListAsync"/>.</summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<PersistedOperationsPage> PersistedOperations(
        IDataContextProviderFactory contextFactory,
        CancellationToken ct,
        PersistedOperationFilter? filter = null,
        int skip = 0,
        int take = 50
    ) => PersistedOperationsService.ForReads(contextFactory).ListAsync(filter, skip, take, ct);

    /// <summary>Kept for callers built against it; calls <see cref="IPersistedOperationsService.GetAsync"/>.</summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<PersistedOperationDto?> PersistedOperation(
        string id,
        IDataContextProviderFactory contextFactory,
        CancellationToken ct,
        string? tenantKey = null
    ) => PersistedOperationsService.ForReads(contextFactory).GetAsync(id, tenantKey, ct);

    /// <summary>Kept for callers built against it; calls <see cref="IPersistedOperationsService.GetHistoryAsync"/>.</summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task<IReadOnlyList<PersistedOperationHistoryDto>> PersistedOperationHistory(
        string id,
        IDataContextProviderFactory contextFactory,
        CancellationToken ct,
        string? tenantKey = null,
        int skip = 0,
        int take = 50
    ) =>
        PersistedOperationsService
            .ForReads(contextFactory)
            .GetHistoryAsync(id, tenantKey, skip, take, ct);
}
