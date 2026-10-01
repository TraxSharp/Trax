using Microsoft.Extensions.Caching.Memory;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// <see cref="IMemoryCache"/>-backed cache wired when the consumer calls
/// <c>WithInMemoryCache()</c>. Keys are <c>(tenantKey, id)</c> with the
/// empty-string sentinel substituted for null tenants.
/// </summary>
internal sealed class InMemoryPersistedOperationCache : IPersistedOperationCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    // Part of every key, so emptying the cache is one increment. The shared IMemoryCache cannot
    // be enumerated; entries from an earlier generation are unreachable and expire with the TTL.
    private long _generation;

    public InMemoryPersistedOperationCache(IMemoryCache cache, PersistedOperationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        _cache = cache;
        _ttl = options.CacheTtl;
    }

    public string? TryGet(string? tenantKey, string id) =>
        _cache.TryGetValue(Key(tenantKey, id), out string? doc) ? doc : null;

    public void Set(string? tenantKey, string id, string document) =>
        _cache.Set(Key(tenantKey, id), document, _ttl);

    public void Invalidate(string? tenantKey, string id) => _cache.Remove(Key(tenantKey, id));

    /// <summary>
    /// Drops every entry. Used when this node may have missed a broadcast change.
    /// </summary>
    public void InvalidateAll() => Interlocked.Increment(ref _generation);

    private string Key(string? tenantKey, string id) =>
        $"trax:po:{Interlocked.Read(ref _generation)}:{tenantKey ?? string.Empty}:{id}";
}
