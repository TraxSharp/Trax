---
title: UsePostgres
description: "Reference for UsePostgres, the PostgreSQL data provider: its overloads, the startup migration, the Npgsql data source and the services it registers."
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 10
package: Trax.Effect.Data.Postgres
namespace: Trax.Effect.Data.Postgres.Extensions
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Extensions/ModelBuilderExtensions.cs
redirect_from:
  - /docs/sdk-reference/configuration/add-postgres-effect
---

# UsePostgres

The PostgreSQL data provider. Persists train metadata, logs, manifests, the work queue, dead letters and the rest
of the `trax` tables in a Postgres database, and migrates its schema on startup.

## Signatures

```csharp fragment
public static TraxEffectBuilderWithData UsePostgres(
    this TraxEffectBuilder configurationBuilder,
    string connectionString
)

public static TraxEffectBuilderWithData UsePostgres(
    this TraxEffectBuilder configurationBuilder,
    string connectionString,
    Action<NpgsqlDataSourceBuilder>? configureDataSource
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `connectionString` | `string` | The Npgsql connection string, such as `"Host=localhost;Database=trax;Username=postgres;Password=password"` |
| `configureDataSource` | `Action<NpgsqlDataSourceBuilder>?` | Called on the data source builder after Trax maps its enums and before `Build()`: pool size, timeouts, multiplexing. `null` leaves the defaults. |

## Returns

`TraxEffectBuilderWithData`, which carries the methods that need a data provider. See
[AddEffects](/docs/sdk-reference/configuration/add-effects#traxeffectbuilderwithdata).

## Behavior

Unless [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) was called earlier in the chain,
`UsePostgres` runs `DatabaseMigrator.Migrate(connectionString)` synchronously before it registers anything, so the
database must be reachable when the host is configured. See
[DatabaseMigrator](/docs/sdk-reference/configuration/database-migrator). It sets `HasDataProvider` and
`HasDatabaseProvider`.

- The migration's scripts wait at most five seconds for a table lock. When one gives up, the migrator runs the
  pending scripts again, up to ten times, then fails startup with Postgres error `55P03`. See
  [Writing Migrations](/docs/contributing/writing-migrations#every-postgres-script-can-run-again).
- Postgres refuses the NUL character in `text` and `jsonb`. Every string Trax writes through this provider has each
  NUL replaced with U+FFFD. A column that identifies a row (a key, a foreign key, an indexed column, a work queue
  entry's external id or subject key) is compared exactly, so its NUL is kept and Postgres refuses the write;
  `WorkQueue.Create` refuses a subject key holding a NUL before that. SQLite and the in-memory provider store NUL
  unchanged.
- When Postgres refuses a run's row because of a value it carries (SQLSTATE `22001`, `22021`, `22P05` or `54000`,
  or text the client cannot encode, such as half of a surrogate pair), the data context raises
  `StoreRefusedContentException`, and Trax writes the row again with the output replaced by
  `{"_unrecorded": true}` and the failure message and stack trace replaced by fixed text, keeping the state, end
  time, failure type, junction and class. An input refused on the run's first write is replaced the same way. The
  original error is logged, and it propagates only when the second write fails too. Lifecycle hooks still receive
  the real output and failure message. Any other failure propagates without a second write.

### The data source

The `NpgsqlDataSource` is built by the container, once per service provider, with
`ModelBuilderExtensions.BuildDataSource(connectionString, configureDataSource)`, and disposed with that provider,
which closes its connection pool. Two providers built from one service collection each get their own. It maps
these enums to their `trax` schema types: `TrainState`, `FailureClass`, `LogLevel`, `ScheduleType`,
`DeadLetterStatus`, `WorkQueueStatus`, `MisfirePolicy`, `JunctionRunKind`, `JunctionRunState` and
`SnapshotOwnerKind`.

`BuildDataSource(connectionString)` and `BuildDataSource(connectionString, configure)` are public, for code that
needs a data source mapped the same way outside the container.

### Registered services

| Service | Lifetime | Role |
|---------|----------|------|
| `NpgsqlDataSource` | Singleton | The data source above |
| `IDbContextFactory<PostgresContext>` | Singleton | Creates `PostgresContext`, Trax's EF Core context over the data source |
| [IDataContext](/docs/sdk-reference/configuration/i-data-context) | Scoped | A `PostgresContext` from the factory |
| `IDataContextProviderFactory` | Singleton | The Postgres data context provider factory, registered as a non-toggleable effect, so it persists every run |
| [ISqlDialect](/docs/sdk-reference/configuration/i-sql-dialect) | Singleton | The Postgres dialect |
| [IQueuedWorkListener](/docs/sdk-reference/configuration/i-queued-work-listener) | Singleton | Hears `trax_queued_work` notifications from every host |
| `IInvokedRunListener`, `ISnapshotNoticeSource` | Singleton | The notifications the state machine runtime waits on |
| `ICheckpointRows` | Singleton, if none is registered | The rows checkpoints are stored in |
| `ITraxFeatureDbConfigurator` | Singleton | A `DelegateFeatureDbConfigurator` that points a feature's own `DbContext` at the same data source (`UseNpgsql`), so it needs no `AddDbContext` of the host's |

## Examples

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres("Host=localhost;Database=trax;Username=postgres;Password=password")
        .AddDataContextLogging()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

With the connection pool tuned:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString, dataSource =>
        {
            dataSource.ConnectionStringBuilder.MaxPoolSize = 50;
            dataSource.ConnectionStringBuilder.MinPoolSize = 5;
            dataSource.ConnectionStringBuilder.ConnectionIdleLifetime = 300;
        })
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [UseSqlite](/docs/sdk-reference/configuration/use-sqlite), [UseInMemory](/docs/sdk-reference/configuration/use-in-memory), [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations)
- [Data Persistence](/docs/effect/effect-providers/data-persistence)
- [SQLite and Postgres](/docs/effect/sqlite-and-postgres)
