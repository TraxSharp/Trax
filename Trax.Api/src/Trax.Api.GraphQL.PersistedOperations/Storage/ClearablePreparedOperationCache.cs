using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

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

    public bool TryGetOperation(string operationId, [NotNullWhen(true)] out Operation? operation) =>
        _cache.TryGet(operationId, out operation);

    public void TryAddOperation(string operationId, Operation operation) =>
        _cache.Add(operationId, operation);

    public void Clear() => _cache.Clear();
}
