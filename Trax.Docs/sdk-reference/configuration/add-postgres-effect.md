---
layout: default
title: UsePostgres
parent: Configuration
grand_parent: SDK Reference
nav_order: 1
---

# UsePostgres

Adds PostgreSQL database support for persisting train metadata, logs, manifests, and dead letters. Automatically migrates the database schema on startup.

## Signature

```csharp
public static TraxEffectBuilder UsePostgres(
    this TraxEffectBuilder effectBuilder,
    string connectionString
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connectionString` | `string` | Yes | PostgreSQL connection string (e.g., `"Host=localhost;Database=trax;Username=postgres;Password=password"`) |

## Returns

`TraxEffectBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres("Host=localhost;Database=trax;Username=postgres;Password=password")
        .AddDataContextLogging()
    )
);
```

## What It Registers

1. Migrates the database schema to the latest version via `DatabaseMigrator`
2. Creates an `NpgsqlDataSource` with enum mappings (`TrainState`, `LogLevel`, `ScheduleType`, `DeadLetterStatus`, `WorkQueueStatus`, `MisfirePolicy`)
3. Registers `IDbContextFactory<PostgresContext>` for creating database contexts
4. Registers `IDataContext` (scoped) for direct database access
5. Enables data context logging support (for [AddDataContextLogging]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-data-context-logging.md %}))
6. Registers `PostgresContextProviderFactory` as a **non-toggleable** effect

## Remarks

- Must be called **before** `AddDataContextLogging` (which requires a data provider to be registered).
- The database migration runs synchronously on startup. Ensure the database server is accessible at application start time.
- For testing/development without a database, use [UseInMemory]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %}) instead.

## Package

```
dotnet add package Trax.Effect.Data.Postgres
```
