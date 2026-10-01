namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Cache the store's lookups in memory, so a request for an id HotChocolate has not cached
    /// yet does not read the database. Off by default.
    /// </summary>
    /// <remarks>
    /// This is the second of two cache layers. HotChocolate always caches the parsed document and
    /// the prepared operation for each id it serves, so with this off the database is read the
    /// first time a node serves an id, and again after a change to any operation empties those
    /// caches. Either way a change reaches other nodes only through
    /// <see cref="UseRabbitMqInvalidation"/>; the TTL here bounds only this layer.
    /// </remarks>
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
    // Created by WithInMemoryCache and handed to its callback.
    internal CacheOptions() { }

    /// <summary>
    /// Time-to-live for cached entries. Defaults to 15 minutes when null. Bounds only the Trax
    /// lookup cache: HotChocolate's document and prepared-operation caches do not expire, so on
    /// more than one node the broadcast from <c>UseRabbitMqInvalidation</c> is what keeps them
    /// current, not this.
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
