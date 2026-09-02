using System.Collections.Concurrent;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Replacements for HotChocolate's parsed-document and prepared-operation caches that can
/// be emptied.
/// </summary>
/// <remarks>
/// HotChocolate assumes a persisted-operation id maps to one document for all time, so its
/// caches are keyed on the id and offer no way to drop an entry: version 16 removed the
/// <c>Clear()</c> both caches used to expose, and evicting the executor does not rebuild
/// one whose schema has not changed. Trax lets an operator re-upload a document under an
/// existing id, so it registers these instead — same contracts, plus the clear the
/// invalidator needs.
/// <para>
/// Registered only when the persisted-operations package is wired in, so a host that does
/// not use the feature keeps HotChocolate's own caches.
/// </para>
/// </remarks>
internal sealed class ClearableDocumentCache(int capacity) : IDocumentCache
{
    private readonly SegmentedCache<string, CachedDocument> _cache = new(capacity);

    public int Capacity => capacity;

    public int Count => _cache.Count;

    public bool TryGetDocument(string documentId, out CachedDocument document) =>
        _cache.TryGet(documentId, out document!);

    public void TryAddDocument(string documentId, CachedDocument document) =>
        _cache.Set(documentId, document);

    public void Clear() => _cache.Clear();
}

/// <inheritdoc cref="ClearableDocumentCache"/>
internal sealed class ClearablePreparedOperationCache(int capacity) : IPreparedOperationCache
{
    private readonly SegmentedCache<string, Operation> _cache = new(capacity);

    public int Capacity => capacity;

    public int Count => _cache.Count;

    public bool TryGetOperation(string operationId, out Operation operation) =>
        _cache.TryGet(operationId, out operation!);

    public void TryAddOperation(string operationId, Operation operation) =>
        _cache.Set(operationId, operation);

    public void Clear() => _cache.Clear();
}

/// <summary>
/// A bounded cache that keeps at most <c>2 * capacity</c> entries.
/// </summary>
/// <remarks>
/// Entries land in a hot generation. When it fills, it becomes the cold generation and a
/// new hot one starts; a hit in cold is promoted back to hot. That keeps recently-used
/// entries without per-entry bookkeeping, which is what these caches need — they are
/// pure optimisation, and both keys (document id, operation id) are low-cardinality.
/// </remarks>
internal sealed class SegmentedCache<TKey, TValue>(int capacity)
    where TKey : notnull
{
    private readonly Lock _sync = new();
    private ConcurrentDictionary<TKey, TValue> _hot = new();
    private ConcurrentDictionary<TKey, TValue> _cold = new();

    public int Count => _hot.Count + _cold.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_hot.TryGetValue(key, out value!))
            return true;

        if (!_cold.TryGetValue(key, out value!))
            return false;

        // Promote so a steady-state working set survives generation turnover.
        Set(key, value);
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        _hot[key] = value;

        if (_hot.Count < capacity)
            return;

        lock (_sync)
        {
            if (_hot.Count < capacity)
                return;

            _cold = _hot;
            _hot = new ConcurrentDictionary<TKey, TValue>();
        }
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
