namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Default cache implementation: every call is a no-op. Wired when the
/// consumer does not call <c>WithInMemoryCache()</c>.
/// </summary>
internal sealed class NoOpPersistedOperationCache : IPersistedOperationCache
{
    public string? TryGet(string? tenantKey, string id) => null;

    public void Set(string? tenantKey, string id, string document) { }

    public void Invalidate(string? tenantKey, string id) { }
}
