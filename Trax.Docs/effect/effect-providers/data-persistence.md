---
layout: default
title: Data Persistence
parent: Effect Providers
grand_parent: Effect
nav_order: 1
---

# Data Persistence

The data persistence effect stores a `Metadata` record for every train execution. Each record captures the train name, state, timing, inputs/outputs, and failure details. See [Metadata](/docs/effect/metadata) for the full field breakdown.

Three backends are available: PostgreSQL for production, SQLite for lightweight/single-server deployments, and InMemory for testing. All three implement the same `IDataContext` interface, so your train code doesn't change between them.

## PostgreSQL

```bash
dotnet add package Trax.Effect.Data.Postgres
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres("Host=localhost;Database=app;Username=postgres;Password=pass")
    )
);
```

On first startup, the Postgres provider runs automatic migrations to create the `trax` schema and its tables (`metadata`, `logs`, `manifests`, `dead_letters`). Subsequent startups apply any pending migrations.

The provider uses Entity Framework Core with Npgsql. Train states and dead letter statuses are mapped to PostgreSQL enum types. Input and output fields use `jsonb` columns. All timestamps are stored in UTC.

### What Gets Persisted

Every `ServiceTrain` execution creates a `Metadata` row:

| Field | Description |
|-------|-------------|
| `Name` | Train class name |
| `TrainState` | Pending -> InProgress -> Completed or Failed |
| `StartTime` / `EndTime` | Execution duration |
| `Input` / `Output` | Serialized JSON (requires [Parameter Effect](parameter-effect.md)) |
| `FailureJunction` | Which junction threw |
| `FailureException` | Exception type |
| `FailureReason` | Error message |
| `StackTrace` | Full stack trace on failure |
| `ParentId` | Links to parent train for [nested trains](/docs/mediator#nested-trains) |
| `ManifestId` | Links to scheduling manifest |

Without the [Parameter Effect](parameter-effect.md), the `Input` and `Output` columns are null. Metadata is still persisted, but without the serialized request/response data.

## SQLite

```bash
dotnet add package Trax.Effect.Data.Sqlite
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseSqlite("Data Source=trax.db")
    )
);
```

File-backed persistence with zero infrastructure. No database server to install or configure. On first startup, the SQLite provider creates the database file and runs migrations. WAL mode is enabled automatically for concurrent read/write performance.

SQLite supports the full scheduler pipeline (ManifestManager, JobDispatcher, MetadataCleanup, DeadLetterCleanup) and all batch operations (`ExecuteUpdateAsync`, `ExecuteDeleteAsync`). The main limitation is single-server only: SQLite does not support advisory locks or `FOR UPDATE SKIP LOCKED`, so multi-server scheduling coordination is not available.

| | PostgreSQL | SQLite | InMemory |
|---|---|---|---|
| Persistence | Yes | Yes | No |
| Infrastructure | Database server | None (file) | None |
| Multi-server scheduling | Yes | No | No |
| Full scheduler trains | Yes | Yes | No (simplified) |
| Batch operations | Yes | Yes | No |

Use SQLite for local development, integration testing with real SQL, or single-server deployments where Postgres is overkill.

## InMemory

```bash
dotnet add package Trax.Effect.Data.InMemory
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
    )
);
```

Uses Entity Framework Core's in-memory provider. No connection string, no migrations, no external dependencies. Data is lost when the process exits.

Use this for unit tests and integration tests where you want metadata tracking without a database:

```csharp
var services = new ServiceCollection();
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
    )
    .AddMediator(typeof(MyTrain).Assembly)
);

var provider = services.BuildServiceProvider();
var context = provider.GetRequiredService<IDataContext>();

// Run a train, then query metadata
var metadata = await context.Metadatas.FirstOrDefaultAsync();
Assert.Equal(TrainState.Completed, metadata.TrainState);
```

## DataContext Logging

Logs the SQL queries that EF Core generates. Useful when debugging persistence issues or inspecting what the data provider is doing:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDataContextLogging(minimumLogLevel: LogLevel.Information)
    )
);
```

Log levels can also be changed at runtime via the Dashboard's Server Settings page. The optional `blacklist` parameter filters out noisy namespaces:

```csharp
.AddDataContextLogging(
    minimumLogLevel: LogLevel.Debug,
    blacklist: ["Microsoft.EntityFrameworkCore.Infrastructure.*"]
)
```

Blacklist entries support exact matches and wildcard patterns.

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging)
