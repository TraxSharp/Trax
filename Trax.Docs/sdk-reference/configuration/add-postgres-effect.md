---
layout: default
title: UsePostgres
description: "Reference for UsePostgres, the PostgreSQL data provider: signatures, connection pool tuning, automatic migrations and the services it registers."
parent: Configuration
grand_parent: SDK Reference
nav_order: 1
---

# UsePostgres

Adds PostgreSQL database support for persisting train metadata, logs, manifests, and dead letters. Automatically migrates the database schema on startup.

## Signatures

```csharp
// Basic: uses default Npgsql data source settings
public static TraxEffectBuilderWithData UsePostgres(
    this TraxEffectBuilder effectBuilder,
    string connectionString
)

// With data source configuration: tune pool size, timeouts, multiplexing, etc.
public static TraxEffectBuilderWithData UsePostgres(
    this TraxEffectBuilder effectBuilder,
    string connectionString,
    Action<NpgsqlDataSourceBuilder> configureDataSource
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connectionString` | `string` | Yes | PostgreSQL connection string (e.g., `"Host=localhost;Database=trax;Username=postgres;Password=password"`) |
| `configureDataSource` | `Action<NpgsqlDataSourceBuilder>` | No | Callback to configure the Npgsql data source builder. Invoked after Trax registers its enum mappings but before `Build()`. Use this to tune connection pool settings, enable multiplexing, or configure other `NpgsqlDataSourceBuilder` options. |

## Returns

`TraxEffectBuilderWithData`, a subclass of `TraxEffectBuilder` that unlocks data-dependent methods like [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging). This provides compile-time safety: methods that require a data provider are only available on the returned type.

## Examples

Basic usage with default settings:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres("Host=localhost;Database=trax;Username=postgres;Password=password")
        .AddDataContextLogging()
    )
);
```

Tuning the connection pool for high-throughput deployments:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString, dataSource =>
        {
            dataSource.ConnectionStringBuilder.MaxPoolSize = 50;
            dataSource.ConnectionStringBuilder.MinPoolSize = 5;
            dataSource.ConnectionStringBuilder.ConnectionIdleLifetime = 300;
        })
    )
);
```

## What It Registers

1. Migrates the database schema to the latest version via `DatabaseMigrator` (unless [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) was called)
2. Registers an `NpgsqlDataSource` singleton with enum mappings (`TrainState`, `FailureClass`, `LogLevel`, `ScheduleType`, `DeadLetterStatus`, `WorkQueueStatus`, `MisfirePolicy`). The container builds it, once per service provider, and disposes it with that provider, which closes its connection pool. Two providers built from the same service collection each get their own
3. Registers `IDbContextFactory<PostgresContext>` for creating database contexts
4. Registers `IDataContext` (scoped) for direct database access
5. Enables data context logging support (for [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging))
6. Registers the Postgres data context provider factory, resolvable as `IDataContextProviderFactory`, as a **non-toggleable** effect

## Remarks

- Returns `TraxEffectBuilderWithData`, which makes `AddDataContextLogging()` available at compile time. Methods that don't require a data provider (like `AddJson()`, `SaveTrainParameters()`) use generic self-type preservation and work on both `TraxEffectBuilder` and `TraxEffectBuilderWithData`.
- The migration's scripts wait at most five seconds for a table lock. A script queued behind a transaction another instance holds open would otherwise hold every write to that table on every instance behind itself. When one gives up, the migrator runs the pending scripts again, up to ten times, and then fails startup with Postgres error `55P03`; start the host again once the long transaction has ended. See [Writing Migrations](/docs/reference/writing-migrations#every-postgres-script-can-run-again).
- The database migration runs synchronously on startup. The database server must be accessible at application start time. To skip migration (e.g., in Lambda runners), call [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) before `UsePostgres()`.
- Postgres cannot store the NUL character: a `text` column refuses `\0` and a `jsonb` column refuses the `\u0000` escape. Every string Trax writes through this provider has each NUL replaced with U+FFFD, the Unicode replacement character, so a train whose output or failure message contains one (a byte buffer rendered as a string, `int.Parse` quoting its input) still records its outcome. SQLite and the in-memory provider store NUL unchanged. A column that identifies a row is the exception: a key, a foreign key, an indexed column, a work queue entry's external id or subject key. It is compared exactly, so replacing its NUL would turn it into a different key; Postgres refuses the write instead, and `WorkQueue.Create` refuses a subject key holding a NUL before it gets that far.
- If Postgres still refuses a run's row because of a value it carries (SQLSTATE `22001`, `22021`, `22P05` or `54000`, or text the client cannot encode, such as half of a surrogate pair), the data context raises `StoreRefusedContentException` and Trax writes the row again with the output replaced by `{"_unrecorded": true}` and the failure message and stack trace replaced by fixed text, keeping the state, end time, failure type, junction and class. An input refused on the run's first write is replaced the same way. The original error is logged, and it propagates only if that second write fails too. Lifecycle hooks still receive the real output and failure message. Any other failure, of the database or of another effect, propagates without a second write, so a row the database already saved in full is never overwritten with placeholders.
- For lightweight deployments or local development without a database server, use [UseSqlite](/docs/sdk-reference/configuration/use-sqlite). For tests without any persistence, use [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect).

## Package

```
dotnet add package Trax.Effect.Data.Postgres
```
