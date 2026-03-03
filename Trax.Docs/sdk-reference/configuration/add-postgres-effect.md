---
layout: default
title: AddPostgresEffect
parent: Configuration
grand_parent: SDK Reference
nav_order: 1
---

# AddPostgresEffect

Adds PostgreSQL database support for persisting train metadata, logs, manifests, and dead letters. Automatically migrates the database schema on startup.

## Signature

```csharp
public static Trax.CoreEffectConfigurationBuilder AddPostgresEffect(
    this Trax.CoreEffectConfigurationBuilder configurationBuilder,
    string connectionString
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connectionString` | `string` | Yes | PostgreSQL connection string (e.g., `"Host=localhost;Database=trax;Username=postgres;Password=password"`) |

## Returns

`Trax.CoreEffectConfigurationBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect("Host=localhost;Database=trax;Username=postgres;Password=password")
    .AddEffectDataContextLogging()
);
```

## What It Registers

1. Migrates the database schema to the latest version via `DatabaseMigrator`
2. Creates an `NpgsqlDataSource` with enum mappings (`TrainState`, `LogLevel`, `ScheduleType`, `DeadLetterStatus`, `WorkQueueStatus`)
3. Registers `IDbContextFactory<PostgresContext>` for creating database contexts
4. Registers `IDataContext` (scoped) for direct database access
5. Enables data context logging support (for [AddEffectDataContextLogging]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-data-context-logging.md %}))
6. Registers `PostgresContextProviderFactory` as a **non-toggleable** effect

## Remarks

- Must be called **before** `AddEffectDataContextLogging` (which requires a data provider to be registered).
- The database migration runs synchronously on startup. Ensure the database server is accessible at application start time.
- For testing/development without a database, use [AddInMemoryEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %}) instead.

## Package

```
dotnet add package Trax.Effect.Data.Postgres
```
