namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

/// <summary>
/// Cache tuning passed to <see cref="PersistedOperationsBuilder.WithInMemoryCache"/>.
/// </summary>
public sealed class CacheOptions
{
    // Created by WithInMemoryCache and handed to its callback.
    internal CacheOptions() { }

    /// <summary>
    /// Time-to-live for the lookup cache's entries. Defaults to the cache's maximum age
    /// (<c>WithCacheMaxAge</c>) when null, and may not exceed it. The broadcast from
    /// <c>UseRabbitMqInvalidation</c> is what keeps every node current; this and the maximum age
    /// bound how long a node that missed a change keeps serving what it had.
    /// </summary>
    public TimeSpan? Ttl { get; private set; }

    /// <summary>
    /// Set the TTL for cache entries.
    /// </summary>
    public CacheOptions WithTtl(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "Cache TTL must be positive.");
        Ttl = ttl;
        return this;
    }
}
