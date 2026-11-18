using System.Collections.Concurrent;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

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
