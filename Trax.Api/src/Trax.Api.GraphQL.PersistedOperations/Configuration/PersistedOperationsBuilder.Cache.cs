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
    /// caches. Either way a change reaches other nodes through
    /// <see cref="UseRabbitMqInvalidation"/>, and <see cref="WithCacheMaxAge"/> bounds how long
    /// any layer keeps a document; the TTL here can only be shorter.
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

    /// <summary>
    /// The longest any cache on a node keeps a persisted operation, counted from when it was read
    /// from the database. Defaults to five minutes.
    /// </summary>
    /// <remarks>
    /// A change reaches every node at once through <see cref="UseRabbitMqInvalidation"/>, or
    /// immediately on a <see cref="SingleNode"/> host. This is the backstop for a change that
    /// did not arrive: a broadcast lost while the broker connection stayed up, or a publish that
    /// failed (which the mutation reports). Such a node serves what it cached for at most this
    /// long, then reads the store again. HotChocolate's own caches never expire; Trax's
    /// replacements for them do. A shorter age means more database reads and recompiles, one
    /// per id per node per period.
    /// </remarks>
    public PersistedOperationsBuilder WithCacheMaxAge(TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(maxAge),
                "The cache's maximum age must be positive."
            );
        _cacheMaxAge = maxAge;
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
