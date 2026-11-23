using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// A <see cref="SegmentedCache{TKey,TValue}"/> whose entries carry the generation and the time
/// their document was read, and are served only while both still hold.
/// </summary>
/// <remarks>
/// An entry is added only inside a request scope, stamped with the generation the request
/// started in and the oldest read time the request saw. Outside a scope nothing is added: a
/// write whose start cannot be placed before the last change cannot be trusted, and a cache
/// miss costs only a read.
/// </remarks>
internal sealed class StampedCache<TValue>(
    int capacity,
    PersistedOperationCacheGeneration generation,
    TimeProvider clock,
    TimeSpan maxAge
)
    where TValue : class
{
    private readonly SegmentedCache<string, Stamped<TValue>> _cache = new(capacity);

    public int Count => _cache.Count;

    public bool TryGet(string key, [NotNullWhen(true)] out TValue? value)
    {
        value = null;
        if (!_cache.TryGet(key, out var entry))
            return false;

        if (entry.Generation != generation.Current || clock.GetElapsedTime(entry.ReadAt) >= maxAge)
            return false;

        // A request served from this entry is serving a document read at entry.ReadAt; anything
        // it caches downstream is no younger than that.
        PersistedOperationRequestScope.For(generation)?.NoteSource(entry.ReadAt);
        value = entry.Value;
        return true;
    }

    public void Add(string key, TValue value)
    {
        if (PersistedOperationRequestScope.For(generation) is not { } scope)
            return;

        _cache.Set(
            key,
            new Stamped<TValue>(
                value,
                scope.StartGeneration,
                scope.SourceTimestamp ?? clock.GetTimestamp()
            )
        );
    }

    /// <summary>
    /// Adds an entry stamped with the generation and time a read outside the request pipeline
    /// started in.
    /// </summary>
    public void Add(string key, TValue value, long stampGeneration, long readAt) =>
        _cache.Set(key, new Stamped<TValue>(value, stampGeneration, readAt));

    public void Clear() => _cache.Clear();
}
