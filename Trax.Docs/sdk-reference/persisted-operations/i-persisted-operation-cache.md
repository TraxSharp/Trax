---
title: IPersistedOperationCache
description: "Reference for IPersistedOperationCache, the optional in-memory lookup cache over the persisted-operation store."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 9
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.Storage
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Storage/IPersistedOperationCache.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Storage/InMemoryPersistedOperationCache.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Storage/NoOpPersistedOperationCache.cs
---

# IPersistedOperationCache

The lookup cache over the persisted-operation store, keyed by tenant and id. It sits under
HotChocolate's document and prepared-operation caches, which are always on.

## Signature

```csharp fragment
public interface IPersistedOperationCache
{
    string? TryGet(string? tenantKey, string id);
    void Set(string? tenantKey, string id, string document);
    void Invalidate(string? tenantKey, string id);
}
```

## Members

| Member | Description |
|--------|-------------|
| `TryGet(tenantKey, id)` | The cached document, or `null` on a miss or when caching is off |
| `Set(tenantKey, id, document)` | Caches the document for the configured TTL. No-op when caching is off |
| `Invalidate(tenantKey, id)` | Drops one entry. No-op when caching is off |

## Behavior

- The registered implementation is a no-op unless the host calls `WithInMemoryCache()` on the
  [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder);
  then it is backed by `IMemoryCache` with entries living for `CacheTtl`.
- A null tenant key is stored as the empty string, as in the table.
- Entries carry the cache generation that was current when their document was read. Every change
  applied on a node starts a new generation, so an entry from before it is never served again, and
  no entry is served past `CacheMaxAge`.

## Example

```csharp compile
var cache = serviceProvider.GetRequiredService<IPersistedOperationCache>();
var document = cache.TryGet(tenantKey: null, "userProfile_v1");
Console.WriteLine(document is null ? "not cached" : "cached");
```

## See also

- [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)
- [PersistedOperationsOptions](/docs/sdk-reference/persisted-operations/persisted-operations-options)
