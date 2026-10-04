using Microsoft.Extensions.Caching.Memory;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// <see cref="IMemoryCache"/>-backed cache wired when the consumer calls
/// <c>WithInMemoryCache()</c>. Keys are <c>(generation, tenantKey, id)</c> with the
/// empty-string sentinel substituted for null tenants.
/// </summary>
/// <remarks>
/// The generation in the key is the one that was current before the document was read, so an
/// entry written by a read that a change overtook is filed under a generation no lookup asks for.
/// The shared <see cref="IMemoryCache"/> cannot be enumerated; entries from an earlier generation
/// are unreachable and expire with the TTL. See
/// <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
/// </remarks>
internal sealed class InMemoryPersistedOperationCache : IPersistedOperationCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private readonly PersistedOperationCacheGeneration _generation;
    private readonly TimeProvider _clock;

    public InMemoryPersistedOperationCache(
        IMemoryCache cache,
        PersistedOperationsOptions options,
        PersistedOperationCacheGeneration generation,
        TimeProvider clock
    )
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(clock);
        _cache = cache;
        _ttl = options.CacheTtl;
        _generation = generation;
        _clock = clock;
    }

    public string? TryGet(string? tenantKey, string id) =>
        TryGet(tenantKey, id, out var document, out _) ? document : null;

    /// <summary>
    /// Caches <paramref name="document"/> as read now, in the current generation. The storage
    /// uses the overload that takes the generation and time its read started in.
    /// </summary>
    public void Set(string? tenantKey, string id, string document) =>
        Set(tenantKey, id, document, _generation.Current, _clock.GetTimestamp());

    public void Invalidate(string? tenantKey, string id) =>
        _cache.Remove(Key(_generation.Current, tenantKey, id));

    /// <summary>
    /// Looks up a document cached in the current generation and younger than the TTL, and when
    /// it was read from the database.
    /// </summary>
    internal bool TryGet(string? tenantKey, string id, out string document, out long readAt)
    {
        document = string.Empty;
        readAt = 0;
        if (
            !_cache.TryGetValue(Key(_generation.Current, tenantKey, id), out Entry? entry)
            || entry is null
            || _clock.GetElapsedTime(entry.ReadAt) >= _ttl
        )
            return false;

        document = entry.Document;
        readAt = entry.ReadAt;
        return true;
    }

    /// <summary>
    /// Caches a document read in <paramref name="generation"/> at <paramref name="readAt"/>. A
    /// change committed since then has advanced the generation, so the entry is never found.
    /// </summary>
    internal void Set(
        string? tenantKey,
        string id,
        string document,
        long generation,
        long readAt
    ) => _cache.Set(Key(generation, tenantKey, id), new Entry(document, readAt), _ttl);

    private static string Key(long generation, string? tenantKey, string id) =>
        $"trax:po:{generation}:{tenantKey ?? string.Empty}:{id}";

    private sealed record Entry(string Document, long ReadAt);
}
