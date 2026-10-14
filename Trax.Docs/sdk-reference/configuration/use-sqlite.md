---
title: UseSqlite
description: "Reference for UseSqlite, the SQLite data provider: the startup migration, WAL mode, how it stores enums and JSON, and the services it registers."
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 13
package: Trax.Effect.Data.Sqlite
namespace:
  - Trax.Effect.Data.Sqlite.Extensions
  - Trax.Effect.Data.Sqlite.Utils
source:
  - Trax.Effect/src/Trax.Effect.Data.Sqlite/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data.Sqlite/Utils/DatabaseMigrator.cs
---

# UseSqlite

The SQLite data provider. Persists the `trax` tables in a SQLite database file, migrates its schema on startup and
switches the database to WAL mode.

## Signature

```csharp fragment
public static TraxEffectBuilderWithData UseSqlite(
    this TraxEffectBuilder configurationBuilder,
    string connectionString
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `connectionString` | `string` | The SQLite connection string, such as `"Data Source=trax.db"` |

## Returns

`TraxEffectBuilderWithData`, which carries the methods that need a data provider. See
[AddEffects](/docs/sdk-reference/configuration/add-effects#traxeffectbuilderwithdata).

## Behavior

`UseSqlite` runs, in order, before it registers anything: the migration (unless
[SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) was called earlier in the chain), then
`PRAGMA journal_mode=WAL`. It sets `HasDataProvider` and `HasDatabaseProvider`, so the scheduler runs its full,
database-backed services on it, as on Postgres.

- Enums are stored as their integers, EF Core's default for SQLite, and JSON as `TEXT`. Raw SQL compares an enum
  column to an integer, such as `0` for `TrainState.Pending`. `TrainState`, `WorkQueueStatus` and `FailureClass`,
  which Trax's own SQLite SQL depends on, pin their values.
- Writes are serialized at the file. There are no advisory locks or `FOR UPDATE SKIP LOCKED`, so dispatch is not
  coordinated across processes sharing one file.
- NUL characters are stored unchanged.

### DatabaseMigrator

`Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator` applies the embedded SQLite scripts.

| Member | Description |
|--------|-------------|
| `Migrate(connectionString)` | Applies every script not yet in the DbUp journal, in name order. Runs synchronously and returns a completed task. The failing script's exception is written to standard output and rethrown unchanged. |
| `CreateEngineWithEmbeddedScripts(connectionString)` | The DbUp `UpgradeEngine` over the embedded scripts. |

Each script runs in its own transaction together with its journal row, so a script that fails partway leaves nothing
behind and runs again cleanly at the next start.

### Registered services

| Service | Lifetime | Role |
|---------|----------|------|
| `IDbContextFactory<SqliteContext>` | Singleton | Creates Trax's EF Core context over the file |
| [IDataContext](/docs/sdk-reference/configuration/i-data-context) | Scoped | A context from the factory |
| `IDataContextProviderFactory` | Singleton | The SQLite data context provider factory, registered as a non-toggleable effect |
| [ISqlDialect](/docs/sdk-reference/configuration/i-sql-dialect) | Singleton | The SQLite dialect |
| [IQueuedWorkListener](/docs/sdk-reference/configuration/i-queued-work-listener) | Singleton | An in-process signal raised when a save through one of these contexts commits queued work |
| `ICheckpointRows` | Singleton, if none is registered | The rows checkpoints are stored in |
| `ITraxFeatureDbConfigurator` | Singleton | A `DelegateFeatureDbConfigurator` that points a feature's own `DbContext` at the same file (`UseSqlite`) |

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseSqlite("Data Source=trax.db")
        .AddDataContextLogging()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
    .AddScheduler()
);
```

## See also

- [SQLite and Postgres](/docs/effect/sqlite-and-postgres), how the two providers differ
- [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [UseInMemory](/docs/sdk-reference/configuration/use-in-memory)
- [Data Persistence](/docs/effect/effect-providers/data-persistence)
