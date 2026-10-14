---
title: UseInMemory
description: Reference for UseInMemory, the in-memory data provider for tests and local development, and InMemoryContextProviderFactory, the factory behind it.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 14
package: Trax.Effect.Data.InMemory
namespace:
  - Trax.Effect.Data.InMemory.Extensions
  - Trax.Effect.Data.InMemory.Services.InMemoryContextFactory
source:
  - Trax.Effect/src/Trax.Effect.Data.InMemory/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data.InMemory/Services/InMemoryContextFactory/InMemoryContexFactory.cs
redirect_from:
  - /docs/sdk-reference/configuration/add-in-memory-effect
---

# UseInMemory

The in-memory data provider. Keeps the `trax` tables in an EF Core in-memory database that lives as long as the
process: nothing to run, nothing kept after the host stops.

## Signature

```csharp fragment
public static TraxEffectBuilderWithData UseInMemory(
    this TraxEffectBuilder configurationBuilder
)
```

## Returns

`TraxEffectBuilderWithData`, which carries the methods that need a data provider. See
[AddEffects](/docs/sdk-reference/configuration/add-effects#traxeffectbuilderwithdata).

## Behavior

Each call creates one `InMemoryDatabaseRoot`, and every context the provider creates, through the factory or the
scoped `IDataContext`, shares it, so they all see the same data. It sets `HasDataProvider` but not
`HasDatabaseProvider`: the scheduler then runs its in-memory set of services and skips the startup work that needs a
database, and no `ISqlDialect` or `IQueuedWorkListener` is registered. NUL characters are stored unchanged.

### Registered services

| Service | Lifetime | Role |
|---------|----------|------|
| `IDataContextProviderFactory` | Singleton | An `InMemoryContextProviderFactory` over the shared root, registered as a non-toggleable effect |
| [IDataContext](/docs/sdk-reference/configuration/i-data-context) | Scoped | A context over the shared root |
| `ICheckpointRows` | Singleton, if none is registered | The rows checkpoints are stored in |
| `ITraxFeatureDbConfigurator` | Singleton | A `DelegateFeatureDbConfigurator` that points a feature's own `DbContext` at the same in-memory store |

### InMemoryContextProviderFactory

```csharp fragment
public class InMemoryContextProviderFactory(InMemoryDatabaseRoot databaseRoot) : IDataContextProviderFactory
```

| Member | Description |
|--------|-------------|
| `Create()` | A new context over `databaseRoot` |
| `CreateDbContextAsync(cancellationToken)` | The same, as a task |

Transactions are accepted and ignored, as the EF Core in-memory provider does.

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [UseSqlite](/docs/sdk-reference/configuration/use-sqlite)
- [Data Persistence](/docs/effect/effect-providers/data-persistence)
