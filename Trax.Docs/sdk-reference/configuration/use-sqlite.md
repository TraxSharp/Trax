---
layout: default
title: UseSqlite
parent: Configuration
grand_parent: SDK Reference
nav_order: 2
---

# UseSqlite

Adds SQLite database support for persisting train metadata, logs, manifests, and dead letters. Automatically migrates the database schema on startup and enables WAL mode for concurrent read/write performance.

## Signature

```csharp
public static TraxEffectBuilderWithData UseSqlite(
    this TraxEffectBuilder effectBuilder,
    string connectionString
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connectionString` | `string` | Yes | SQLite connection string (e.g., `"Data Source=trax.db"`) |

## Returns

`TraxEffectBuilderWithData`, a subclass of `TraxEffectBuilder` that unlocks data-dependent methods like [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging). This provides compile-time safety: methods that require a data provider are only available on the returned type.

## Examples

Basic usage with a file-backed database:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseSqlite("Data Source=trax.db")
        .AddDataContextLogging()
    )
);
```

With the full Trax stack (effects, mediator, scheduler):

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseSqlite("Data Source=trax.db")
        .AddDataContextLogging()
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .Schedule<IMyTrain>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

## What It Registers

1. Migrates the database schema via `DatabaseMigrator` (unless [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) was called)
2. Enables WAL mode (`PRAGMA journal_mode=WAL`) for concurrent read/write performance
3. Registers `IDbContextFactory<SqliteContext>` for creating database contexts
4. Registers `IDataContext` (scoped) for direct database access
5. Enables data context logging support (for [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging))
6. Registers `SqliteContextProviderFactory` as a **non-toggleable** effect
7. Registers `SqliteSqlDialect` as `ISqlDialect` for provider-specific SQL

## SQLite vs Postgres

SQLite is a lightweight, zero-infrastructure alternative to PostgreSQL. Use it for local development, integration testing, or single-server deployments where you don't want to run a database server.

| Feature | SQLite | PostgreSQL |
|---------|--------|------------|
| Infrastructure | None (file-based) | Requires running server |
| Persistence | Yes | Yes |
| Full scheduler support | Yes | Yes |
| Multi-server scheduling | No | Yes (advisory locks) |
| Concurrent writes | Serialized (WAL mode) | Full concurrency |
| FOR UPDATE SKIP LOCKED | No (serialized writes) | Yes |

SQLite sets `HasDatabaseProvider = true`, which means it gets the full `ManifestManagerTrain` (not the simplified InMemory version), `JobDispatcherTrain`, `MetadataCleanupTrain`, and all other database-dependent scheduler features.

Multi-server coordination (advisory locks, concurrent job dequeue across processes) is not supported. SQLite serializes writes at the file level, which prevents duplicate job dispatch within a single process but does not coordinate across multiple processes.

## Remarks

- Returns `TraxEffectBuilderWithData`, which makes `AddDataContextLogging()` available at compile time.
- The database migration runs synchronously on startup. To skip migration (e.g., in Lambda runners), call [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) before `UseSqlite()`.
- SQLite stores enum values as strings (not native database enums like PostgreSQL) and JSON data as TEXT (not JSONB). EF Core handles the serialization transparently.
- For production multi-server deployments, use [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect). For tests without any persistence, use [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect).

## Package

```
dotnet add package Trax.Effect.Data.Sqlite
```

## SDK Reference

> [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) | [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging)
