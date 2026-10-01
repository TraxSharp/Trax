using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// Wraps a real <see cref="IPersistedOperationsService"/>, records which methods were called,
/// and throws from the ones a test names, to stand in for a database that has gone away.
/// </summary>
public sealed class ScriptedPersistedOperationsService(IPersistedOperationsService inner)
    : IPersistedOperationsService
{
    public List<string> Calls { get; } = [];

    public HashSet<string> Throwing { get; } = [];

    public Task<PersistedOperationsPage> ListAsync(
        PersistedOperationFilter? filter,
        int skip,
        int take,
        CancellationToken ct
    ) => Run(nameof(ListAsync), () => inner.ListAsync(filter, skip, take, ct));

    public Task<PersistedOperationDto?> GetAsync(
        string id,
        string? tenantKey,
        CancellationToken ct
    ) => Run(nameof(GetAsync), () => inner.GetAsync(id, tenantKey, ct));

    public Task<IReadOnlyList<PersistedOperationHistoryDto>> GetHistoryAsync(
        string id,
        string? tenantKey,
        int skip,
        int take,
        CancellationToken ct
    ) => Run(nameof(GetHistoryAsync), () => inner.GetHistoryAsync(id, tenantKey, skip, take, ct));

    public Task<UploadPersistedOperationPayload> UploadAsync(
        UploadPersistedOperationInput input,
        CancellationToken ct
    ) => Run(nameof(UploadAsync), () => inner.UploadAsync(input, ct));

    public Task<DeactivatePersistedOperationPayload> DeactivateAsync(
        DeactivatePersistedOperationInput input,
        CancellationToken ct
    ) => Run(nameof(DeactivateAsync), () => inner.DeactivateAsync(input, ct));

    public Task<RestorePersistedOperationPayload> RestoreAsync(
        RestorePersistedOperationInput input,
        CancellationToken ct
    ) => Run(nameof(RestoreAsync), () => inner.RestoreAsync(input, ct));

    private Task<T> Run<T>(string method, Func<Task<T>> call)
    {
        lock (Calls)
            Calls.Add(method);
        return Throwing.Contains(method)
            ? throw new InvalidOperationException("the database is unreachable")
            : call();
    }
}
