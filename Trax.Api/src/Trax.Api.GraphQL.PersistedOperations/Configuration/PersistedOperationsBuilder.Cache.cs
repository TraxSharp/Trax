namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Enable an in-memory cache layer over the storage. Default is no cache:
    /// every request hits the database. Caching is purely an optimization;
    /// the default path is correct for almost every deployment.
    /// </summary>
    public PersistedOperationsBuilder WithInMemoryCache(Action<CacheOptions>? configure = null)
    {
        if (_cacheConfigured)
            throw new InvalidOperationException("WithInMemoryCache configured more than once.");

        _cacheConfigured = true;
        _cacheEnabled = true;

        if (configure is not null)
        {
            var cacheOpts = new CacheOptions();
            configure(cacheOpts);
            if (cacheOpts.Ttl is { } ttl)
                _cacheTtl = ttl;
        }

        return this;
    }
}

/// <summary>
/// Cache tuning passed to <see cref="PersistedOperationsBuilder.WithInMemoryCache"/>.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// Time-to-live for cached entries. Defaults to 15 minutes when null.
    /// Acts as a backstop only; broadcast invalidation is the primary
    /// invalidation mechanism for multi-node deployments.
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
