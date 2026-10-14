---
title: AddPersistedOperationStore
description: "Reference for AddPersistedOperationStore and PersistedOperationStoreBuilder, which register the persisted-operation store on a host with no GraphQL server."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 4
package: Trax.Api.GraphQL.PersistedOperations
namespace:
  - Trax.Api.GraphQL.PersistedOperations.Extensions
  - Trax.Api.GraphQL.PersistedOperations.Configuration
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Extensions/ServiceCollectionPersistedOperationsExtensions.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationStoreBuilder.cs
---

# AddPersistedOperationStore

Registers [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store)
and [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service)
on a host that does not serve GraphQL: a CI manifest uploader, admin tooling, a console client.
Declared on the static class `ServiceCollectionPersistedOperationsExtensions`; `configure` receives a
`PersistedOperationStoreBuilder`.

## Signatures

```csharp fragment
public static IServiceCollection AddPersistedOperationStore(
    this IServiceCollection services,
    Action<PersistedOperationStoreBuilder> configure);

// Obsolete: a compile error.
public static IServiceCollection AddPersistedOperationStore(
    this IServiceCollection services,
    string databaseConnectionString);

// Obsolete.
public static IServiceCollection AddPersistedOperationStore(
    this IServiceCollection services,
    string databaseConnectionString,
    string rabbitMqInvalidationConnectionString);

public sealed class PersistedOperationStoreBuilder
{
    public PersistedOperationStoreBuilder UseRabbitMqInvalidation(string connectionString);
    public PersistedOperationStoreBuilder SingleNode();
}
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `services` | `IServiceCollection` | The service collection. The host also registers the Trax data layer (`AddTrax(t => t.AddEffects(e => e.UsePostgres(...)))`), which the store reads and writes through |
| `configure` | `Action<PersistedOperationStoreBuilder>` | Says how a change made through the store reaches the GraphQL nodes |

| `PersistedOperationStoreBuilder` method | Description |
|-----------------------------------------|-------------|
| `UseRabbitMqInvalidation(connectionString)` | Broadcast every change over the broker the GraphQL nodes use with their own `UseRabbitMqInvalidation`, so each empties its caches. Throws `ArgumentException` for a blank connection string |
| `SingleNode()` | Declare that no other process caches these operations: this one serves them itself, or nothing serves them while it writes. Nothing checks the claim; a GraphQL node elsewhere sees a change only when its cached entry reaches its maximum age |

## Returns

The same `IServiceCollection`.

## Behavior

- Throws `ArgumentNullException` for a `null` argument, and `InvalidOperationException` when
  `configure` calls neither builder method or calls both. Both are thrown when
  `AddPersistedOperationStore` runs.
- The one-string overload is a compile error; called through reflection it throws as if
  `configure` did nothing. The two-string overload ignores the first string and forwards to
  `store => store.UseRabbitMqInvalidation(rabbitMqInvalidationConnectionString)`.
- Called alongside [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
  in one host, the GraphQL registration's validator replaces the no-op one.

### Registered services

| Service | Lifetime | Implementation |
|---------|----------|----------------|
| `PersistedOperationsOptions` | Singleton | Built with `SingleNode()` or the broker |
| `IPersistedOperationStore` | Singleton | `DbPersistedOperationStorage` |
| `IPersistedOperationsService` | Singleton (TryAdd) | The shared management service |
| `IPersistedOperationValidator` | Singleton (TryAdd) | `NoOpPersistedOperationValidator` |
| `IPersistedOperationCache` | Singleton (TryAdd) | A no-op cache |
| `IPersistedOperationBroadcaster` | Singleton (TryAdd) | RabbitMQ with `UseRabbitMqInvalidation`, a no-op with `SingleNode` |
| `TimeProvider` | Singleton (TryAdd) | `TimeProvider.System` |

## Example

```csharp compile
services.AddPersistedOperationStore(store => store.UseRabbitMqInvalidation(rabbitMqConnectionString));
```

## See also

- [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store)
- [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service)
- [One node or many](/docs/persisted-operations#one-node-or-many)
