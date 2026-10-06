using System.Collections.Concurrent;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Replacements for HotChocolate's parsed-document and prepared-operation caches that can
/// be emptied, never return an entry older than the last change, and never return one older
/// than the maximum age.
/// </summary>
/// <remarks>
/// HotChocolate assumes a persisted-operation id maps to one document for all time, so its
/// caches are keyed on the id, offer no way to drop an entry, and never expire: version 16
/// removed the <c>Clear()</c> both caches used to expose, and evicting the executor does not
/// rebuild one whose schema has not changed. Trax lets an operator re-upload or deactivate a
/// document under an existing id, so it registers these instead: same contracts, plus the
/// generation stamp and maximum age described in
/// <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
/// <para>
/// Registered only when the persisted-operations package is wired in, so a host that does
/// not use the feature keeps HotChocolate's own caches.
/// </para>
/// </remarks>
internal sealed class ClearableDocumentCache(
    int capacity,
    PersistedOperationCacheGeneration generation,
    TimeProvider clock,
    TimeSpan maxAge
) : IDocumentCache
{
    private readonly StampedCache<CachedDocument> _cache = new(capacity, generation, clock, maxAge);

    public int Capacity => capacity;

    public int Count => _cache.Count;

    public bool TryGetDocument(string documentId, out CachedDocument document) =>
        _cache.TryGet(documentId, out document!);

    /// <summary>
    /// Caches a document under <paramref name="documentId"/>. A document the store did not supply
    /// is cached only under its own hash: the id a request names is the store's to define, and an
    /// inline document cached under it would become what that id runs.
    /// </summary>
    public void TryAddDocument(string documentId, CachedDocument document)
    {
        if (
            !document.IsPersisted
            && !string.Equals(documentId, document.Hash.Value, StringComparison.Ordinal)
        )
            return;

        _cache.Add(documentId, document);
    }

    public void Clear() => _cache.Clear();
}

/// <inheritdoc cref="ClearableDocumentCache"/>
internal sealed class ClearablePreparedOperationCache(
    int capacity,
    PersistedOperationCacheGeneration generation,
    TimeProvider clock,
    TimeSpan maxAge
) : IPreparedOperationCache
{
    private readonly StampedCache<Operation> _cache = new(capacity, generation, clock, maxAge);

    public int Capacity => capacity;

    public int Count => _cache.Count;

    public bool TryGetOperation(string operationId, out Operation operation) =>
        _cache.TryGet(operationId, out operation!);

    public void TryAddOperation(string operationId, Operation operation) =>
        _cache.Add(operationId, operation);

    public void Clear() => _cache.Clear();
}

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

    public bool TryGet(string key, out TValue? value)
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

/// <summary>A cached value, the generation it was read in, and when it was read.</summary>
internal readonly record struct Stamped<TValue>(TValue Value, long Generation, long ReadAt);

/// <summary>
/// A bounded cache that keeps at most <c>2 * capacity</c> entries.
/// </summary>
/// <remarks>
/// Entries land in a hot generation. When it fills, it becomes the cold generation and a
/// new hot one starts; a hit in cold is promoted back to hot. That keeps recently-used
/// entries without per-entry bookkeeping, which is what these caches need: they are
/// pure optimisation, and both keys (document id, operation id) are low-cardinality.
/// <para>
/// Reads take no lock. Writes take one, so the hot generation never holds more than
/// <c>capacity</c> entries and the bound holds under concurrent writers; writes happen only
/// on a miss, so the lock is off the steady-state path.
/// </para>
/// </remarks>
internal sealed class SegmentedCache<TKey, TValue>(int capacity)
    where TKey : notnull
{
    private readonly Lock _sync = new();
    private volatile ConcurrentDictionary<TKey, TValue> _hot = new();
    private volatile ConcurrentDictionary<TKey, TValue> _cold = new();

    public int Count => _hot.Count + _cold.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_hot.TryGetValue(key, out value!))
            return true;

        var cold = _cold;
        if (!cold.TryGetValue(key, out value!))
            return false;

        // Promote so a steady-state working set survives generation turnover, unless the
        // generations turned over or were cleared since the read.
        lock (_sync)
        {
            if (ReferenceEquals(cold, _cold) && !_hot.ContainsKey(key))
                SetLocked(key, value);
        }
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_sync)
            SetLocked(key, value);
    }

    private void SetLocked(TKey key, TValue value)
    {
        if (_hot.Count >= capacity && !_hot.ContainsKey(key))
        {
            _cold = _hot;
            _hot = new ConcurrentDictionary<TKey, TValue>();
        }

        _hot[key] = value;
    }

    public void Clear()
    {
        lock (_sync)
        {
            _hot = new ConcurrentDictionary<TKey, TValue>();
            _cold = new ConcurrentDictionary<TKey, TValue>();
        }
    }
}
