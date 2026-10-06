namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Cache layer over <see cref="IPersistedOperationStore"/>. The default
/// registration is a no-op; an in-memory implementation is wired when the
/// consumer opts in via <c>WithInMemoryCache()</c>.
/// </summary>
public interface IPersistedOperationCache
{
    /// <summary>
    /// Look up a cached document. Returns null on miss or when caching is disabled.
    /// </summary>
    string? TryGet(string? tenantKey, string id);

    /// <summary>
    /// Cache a document for the configured TTL. No-op when caching is disabled.
    /// </summary>
    void Set(string? tenantKey, string id, string document);

    /// <summary>
    /// Drop a single cached entry. No-op when caching is disabled.
    /// </summary>
    void Invalidate(string? tenantKey, string id);
}
