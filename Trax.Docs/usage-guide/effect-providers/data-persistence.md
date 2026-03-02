---
layout: default
title: Data Persistence
parent: Effect Providers
grand_parent: Usage Guide
nav_order: 1
---

# Data Persistence

The data persistence effect stores a `Metadata` record for every train execution. Each record captures the train name, state, timing, inputs/outputs, and failure details. See [Metadata](../../concepts/metadata.md) for the full field breakdown.

Two backends are available: PostgreSQL for production and InMemory for testing. Both implement the same `IDataContext` interface, so your train code doesn't change between them.

## PostgreSQL

```bash
dotnet add package Trax.Effect.Data.Postgres
```

```csharp
services.AddTrax.CoreEffects(options =>
    options.AddPostgresEffect("Host=localhost;Database=app;Username=postgres;Password=pass")
);
```

*API Reference: [AddPostgresEffect]({{ site.baseurl }}{% link api-reference/configuration/add-postgres-effect.md %})*

On first startup, the Postgres provider runs automatic migrations to create the `trax` schema and its tables (`metadata`, `logs`, `manifests`, `dead_letters`). Subsequent startups apply any pending migrations.

The provider uses Entity Framework Core with Npgsql. Train states and dead letter statuses are mapped to PostgreSQL enum types. Input and output fields use `jsonb` columns. All timestamps are stored in UTC.

### What Gets Persisted

Every `ServiceTrain` execution creates a `Metadata` row:

| Field | Description |
|-------|-------------|
| `Name` | Train class name |
| `TrainState` | Pending → InProgress → Completed or Failed |
| `StartTime` / `EndTime` | Execution duration |
| `Input` / `Output` | Serialized JSON (requires [Parameter Effect](parameter-effect.md)) |
| `FailureStep` | Which step threw |
| `FailureException` | Exception type |
| `FailureReason` | Error message |
| `StackTrace` | Full stack trace on failure |
| `ParentId` | Links to parent train for [nested trains](../mediator.md#nested-trains) |
| `ManifestId` | Links to scheduling manifest |

Without the [Parameter Effect](parameter-effect.md), the `Input` and `Output` columns are null—metadata is still persisted, but without the serialized request/response data.

## InMemory

```bash
dotnet add package Trax.Effect.Data.InMemory
```

```csharp
services.AddTrax.CoreEffects(options =>
    options.AddInMemoryEffect()
);
```

*API Reference: [AddInMemoryEffect]({{ site.baseurl }}{% link api-reference/configuration/add-in-memory-effect.md %})*

Uses Entity Framework Core's in-memory provider. No connection string, no migrations, no external dependencies. Data is lost when the process exits.

Use this for unit tests and integration tests where you want metadata tracking without a database:

```csharp
var services = new ServiceCollection();
services.AddTrax.CoreEffects(options =>
    options
        .AddInMemoryEffect()
        .AddServiceTrainBus(typeof(MyTrain).Assembly)
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
services.AddTrax.CoreEffects(options =>
    options
        .AddPostgresEffect(connectionString)
        .AddEffectDataContextLogging(minimumLogLevel: LogLevel.Information)
);
```

Log levels can also be changed at runtime via the Dashboard's Server Settings page. The optional `blacklist` parameter filters out noisy namespaces:

```csharp
.AddEffectDataContextLogging(
    minimumLogLevel: LogLevel.Debug,
    blacklist: ["Microsoft.EntityFrameworkCore.Infrastructure.*"]
)
```

Blacklist entries support exact matches and wildcard patterns.

*API Reference: [AddEffectDataContextLogging]({{ site.baseurl }}{% link api-reference/configuration/add-effect-data-context-logging.md %})*
