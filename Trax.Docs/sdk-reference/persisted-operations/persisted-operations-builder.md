---
title: PersistedOperationsBuilder
description: "Reference for PersistedOperationsBuilder: enforcement, the allowlist, single-node or RabbitMQ invalidation, cache options and machine document retention."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 2
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.Configuration
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Enforcement.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Allowlist.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Broadcasting.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Cache.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Exposure.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.MachineDocuments.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Database.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Build.cs
---

# PersistedOperationsBuilder

The options passed to [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations). Every method returns the builder.

## Signature

```csharp fragment
public sealed partial class PersistedOperationsBuilder
{
    public PersistedOperationsBuilder RequirePersisted(bool require = true);
    public PersistedOperationsBuilder LogNonPersistedRequests(bool log = true);
    public PersistedOperationsBuilder AllowOperations(params string[] operationNames);
    public PersistedOperationsBuilder AllowOperationsMatching(Func<string, bool> predicate);
    public PersistedOperationsBuilder DisableIntrospection();
    public PersistedOperationsBuilder WithInMemoryCache(Action<CacheOptions>? configure = null);
    public PersistedOperationsBuilder WithCacheMaxAge(TimeSpan maxAge);
    public PersistedOperationsBuilder UseRabbitMqInvalidation(string connectionString);
    public PersistedOperationsBuilder SingleNode();
    public PersistedOperationsBuilder ExposeOperationsNamespace(bool expose = true);
    public PersistedOperationsBuilder KeepOlderMachineDocumentsFor(TimeSpan grace);
    public PersistedOperationsBuilder UseDatabase(string connectionString); // obsolete
}

public sealed class CacheOptions
{
    public TimeSpan? Ttl { get; }
    public CacheOptions WithTtl(TimeSpan ttl);
}
```

## Members

| Method | Default | Purpose |
|---|---|---|
| `RequirePersisted(bool require = true)` | `true` | Refuse inline documents. `false` is shadow mode, which needs `LogNonPersistedRequests(true)`. |
| `LogNonPersistedRequests(bool log = true)` | `false` | Log every inline-document request at Information. |
| `AllowOperations(params string[] names)` | empty | Operation names that bypass enforcement. Case-sensitive. Matched against the caller-supplied `operationName`, not the document: a convenience for trusted networks, not a security control. |
| `AllowOperationsMatching(Func<string, bool>)` | empty | Predicate form, such as `id => id.StartsWith("dev_")`. The predicate sees the caller-supplied `operationName` (or `documentId`), never the document, so it is as narrow as the network in front of the endpoint. |
| `DisableIntrospection()` | introspection allowed | Inline introspection requests are refused like any other inline document. |
| `WithInMemoryCache(Action<CacheOptions>?)` | no cache | Cache the store's lookups in `IMemoryCache`, under the document and prepared-operation caches, which are always on. Without it the database is read when a node first serves an id, after a change empties those caches, and when an entry reaches its maximum age. |
| `WithCacheMaxAge(TimeSpan maxAge)` | 5 minutes | The longest any cache on a node keeps a persisted operation, counted from when its document was read from the database. The backstop for a change that did not reach the node: it serves what it cached for at most this long, then reads the store again. Must be positive. |
| `UseRabbitMqInvalidation(string connectionString)` | none | Broadcast every change over a RabbitMQ fanout so every node empties its caches. Required when more than one node serves the endpoint. Works with or without `WithInMemoryCache()`. A publish the broker does not confirm is reported as `CHANGE_NOT_BROADCAST`. |
| `SingleNode()` | none | Declare that one process serves the endpoint and writes the store, so no broadcast is needed. |
| `ExposeOperationsNamespace(bool expose = true)` | `true` | Whether the `operations` namespace, and the management fields in it, is added to the schema. `false` keeps storage, enforcement, the cache and invalidation. |
| `KeepOlderMachineDocumentsFor(TimeSpan grace)` | 7 days | How long an older version's [machine documents](/docs/sdk-reference/graphql-api/machines#persisted-documents) stay active after a node registers a newer version's. A node still running the older version keeps serving them during a rolling deploy. Each node registers its own documents when it starts and again every hour while it runs, and a registration made once its own have been stored this long deactivates the older ones. Must be positive. |

`UseDatabase(string)` is obsolete, hidden from IntelliSense, and does nothing beyond throwing `ArgumentException` for a blank string: storage reads and writes `trax.persisted_operation` through the Trax data context that `AddEffects(e => e.UsePostgres(...))` registers.

## CacheOptions

| Method | Default | Purpose |
|---|---|---|
| `WithTtl(TimeSpan ttl)` | the maximum age | Entry lifetime for the Trax lookup cache, read back from `Ttl`. Must be positive (`ArgumentOutOfRangeException`) and may not exceed `WithCacheMaxAge` (`InvalidOperationException` when the options are built). |

## Behavior

- Exactly one of `SingleNode()` and `UseRabbitMqInvalidation(...)` is required. See [One node or many](/docs/persisted-operations#one-node-or-many).
- `UseRabbitMqInvalidation` throws `ArgumentException` for a blank connection string; `WithCacheMaxAge` and `KeepOlderMachineDocumentsFor` throw `ArgumentOutOfRangeException` for a value that is not positive; a second `WithInMemoryCache` throws `InvalidOperationException`.
- The remaining checks run when `UsePersistedOperations` builds the options and throw `InvalidOperationException`; the [UsePersistedOperations validation table](/docs/sdk-reference/persisted-operations/use-persisted-operations#validation) lists them.
- What the allowlist does and does not protect: [Allowlist and dev carve-outs](/docs/persisted-operations#allowlist-and-dev-carve-outs).

## Example

```csharp compile
services.AddTraxGraphQL(graphql => graphql
    .AllowAnonymousOperations()
    .UsePersistedOperations(opts => opts
        .RequirePersisted(true)
        .LogNonPersistedRequests(true)
        .AllowOperations("playground_smoke_test")
        .AllowOperationsMatching(id => id.StartsWith("dev_"))
        .WithCacheMaxAge(TimeSpan.FromMinutes(5))
        .WithInMemoryCache(c => c.WithTtl(TimeSpan.FromMinutes(2)))
        .UseRabbitMqInvalidation(rabbitMqConnectionString)
    )
);
```

## See also

- [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
- [PersistedOperationsOptions](/docs/sdk-reference/persisted-operations/persisted-operations-options)
- [Persisted Operations](/docs/persisted-operations)
