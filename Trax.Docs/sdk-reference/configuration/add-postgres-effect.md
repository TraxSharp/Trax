---
layout: default
title: UsePostgres
parent: Configuration
grand_parent: SDK Reference
nav_order: 1
---

# UsePostgres

Adds PostgreSQL database support for persisting train metadata, logs, manifests, and dead letters. Automatically migrates the database schema on startup.

## Signatures

```csharp
// Basic — uses default Npgsql data source settings
public static TraxEffectBuilderWithData UsePostgres(
    this TraxEffectBuilder effectBuilder,
    string connectionString
)

// With data source configuration — tune pool size, timeouts, multiplexing, etc.
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

`TraxEffectBuilderWithData` — a subclass of `TraxEffectBuilder` that unlocks data-dependent methods like [AddDataContextLogging]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-data-context-logging.md %}). This provides compile-time safety: methods that require a data provider are only available on the returned type.

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

1. Migrates the database schema to the latest version via `DatabaseMigrator` (unless [SkipMigrations]({{ site.baseurl }}{% link sdk-reference/configuration/skip-migrations.md %}) was called)
2. Creates an `NpgsqlDataSource` with enum mappings (`TrainState`, `LogLevel`, `ScheduleType`, `DeadLetterStatus`, `WorkQueueStatus`, `MisfirePolicy`)
3. Registers `IDbContextFactory<PostgresContext>` for creating database contexts
4. Registers `IDataContext` (scoped) for direct database access
5. Enables data context logging support (for [AddDataContextLogging]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-data-context-logging.md %}))
6. Registers `PostgresContextProviderFactory` as a **non-toggleable** effect

## Remarks

- Returns `TraxEffectBuilderWithData`, which makes `AddDataContextLogging()` available at compile time. Methods that don't require a data provider (like `AddJson()`, `SaveTrainParameters()`) use generic self-type preservation and work on both `TraxEffectBuilder` and `TraxEffectBuilderWithData`.
- The database migration runs synchronously on startup. Ensure the database server is accessible at application start time. To skip migration (e.g., in Lambda runners), call [SkipMigrations]({{ site.baseurl }}{% link sdk-reference/configuration/skip-migrations.md %}) before `UsePostgres()`.
- For testing/development without a database, use [UseInMemory]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %}) instead.

## Package

```
dotnet add package Trax.Effect.Data.Postgres
```
