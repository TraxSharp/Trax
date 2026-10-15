---
title: PersistedOperationsOptions
description: "Reference for PersistedOperationsOptions, the resolved persisted-operation configuration that PersistedOperationsBuilder produces and registers."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 3
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.Configuration
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsOptions.cs
---

# PersistedOperationsOptions

The resolved configuration [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)
builds. [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
and [AddPersistedOperationStore](/docs/sdk-reference/persisted-operations/add-persisted-operation-store)
register it as a singleton. It has no public constructor and no public setters.

## Signature

```csharp fragment
public sealed class PersistedOperationsOptions
{
    public bool ManagementServed { get; }
    public bool RequirePersisted { get; }
    public bool LogNonPersistedRequests { get; }
    public IReadOnlySet<string> AllowedOperationNames { get; }
    public IReadOnlyList<Func<string, bool>> AllowOperationPredicates { get; }
    public bool AllowIntrospection { get; }
    public bool CacheEnabled { get; }
    public TimeSpan CacheTtl { get; }
    public TimeSpan CacheMaxAge { get; }
    public string? RabbitMqConnectionString { get; }
    public bool SingleNode { get; }
    public string DatabaseConnectionString { get; } // obsolete
}
```

## Properties

| Property | Default | Set by | Description |
|----------|---------|--------|-------------|
| `ManagementServed` | `false` | `GateManagement`, `GateManagementToAuthenticatedUsers` | Whether documents are managed through a Trax surface, the `persistedOperations` fields and the dashboard's pages: true once a management gate is declared |
| `RequirePersisted` | `true` | `RequirePersisted` | Whether inline documents are refused with `PERSISTED_OPERATION_REQUIRED` |
| `LogNonPersistedRequests` | `false` | `LogNonPersistedRequests` | Whether inline-document requests are logged at Information |
| `AllowedOperationNames` | empty | `AllowOperations` | Operation names that bypass enforcement, compared ordinally |
| `AllowOperationPredicates` | empty | `AllowOperationsMatching` | Predicates over the operation name, or the document id when there is no name |
| `AllowIntrospection` | `true` | `DisableIntrospection` | Whether a document selecting only `__schema`, `__type` or `__typename` bypasses enforcement |
| `CacheEnabled` | `false` | `WithInMemoryCache` | Whether the store's lookups are also cached in memory |
| `CacheTtl` | `CacheMaxAge` | `CacheOptions.WithTtl` | The lookup cache's entry lifetime; never more than `CacheMaxAge` |
| `CacheMaxAge` | 5 minutes | `WithCacheMaxAge` | The longest any persisted-operation cache on a node keeps an entry, counted from when its document was read |
| `RabbitMqConnectionString` | `null` | `UseRabbitMqInvalidation` | The broker changes are broadcast on; `null` only when `SingleNode` is `true` |
| `SingleNode` | `false` | `SingleNode` | Whether the host declared that one process serves the endpoint and writes the store |
| `DatabaseConnectionString` | `""` | nothing | Obsolete and always empty |

## Example

```csharp compile
public class PersistedOperationsStatus(PersistedOperationsOptions options)
{
    public string Describe() =>
        options.RequirePersisted
            ? $"enforced, cached for at most {options.CacheMaxAge}"
            : "shadow mode";
}
```

## See also

- [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)
- [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
