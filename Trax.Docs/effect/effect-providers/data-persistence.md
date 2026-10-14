---
title: Data Persistence
description: "The data persistence effect that stores a Metadata row per run, on PostgreSQL, SQLite or InMemory, with automatic migrations and DataContext logging."
type: reference
area: data
nav_order: 1
source:
  - Trax.Effect/src/Trax.Effect.Data.Postgres/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data.Sqlite/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data.InMemory/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data/Extensions/ServiceExtensions.cs
---

# Data Persistence

The data persistence effect stores a `Metadata` row for every train run: the train's canonical name, its state (`Pending`, `InProgress`, `Completed`, `Failed` or `Cancelled`), timing, input and output, and failure details. [Metadata](/docs/effect/metadata) lists every field. The `Input` and `Output` columns stay null unless the [parameter effect](/docs/effect/effect-providers/parameter-effect) is registered.

There are three backends: PostgreSQL, SQLite for a single server, and InMemory for tests. All three implement `IDataContext`, so train code does not change between them.

## PostgreSQL

```bash
dotnet add package Trax.Effect.Data.Postgres
```

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres("Host=localhost;Database=app;Username=postgres;Password=pass")
    )
);
```

On first startup, the Postgres provider runs automatic migrations to create the `trax` schema and its tables (`metadata`, `log`, `manifest`, `dead_letter`, among others). Subsequent startups apply any pending migrations.

The provider uses Entity Framework Core with Npgsql. Train states and dead letter statuses are mapped to PostgreSQL enum types. Input and output fields use `jsonb` columns. All timestamps are stored in UTC.

## SQLite

```bash
dotnet add package Trax.Effect.Data.Sqlite
```

```csharp compile
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseSqlite("Data Source=trax.db")
    )
);
```

File-backed persistence with no database server. On first startup, the SQLite provider creates the database file and runs migrations. It turns on WAL mode.

SQLite supports the full scheduler pipeline (ManifestManager, JobDispatcher, MetadataCleanup, DeadLetterCleanup) and all batch operations (`ExecuteUpdateAsync`, `ExecuteDeleteAsync`). It is single-server only: SQLite has no advisory locks or `FOR UPDATE SKIP LOCKED`, so schedulers on several servers cannot coordinate.

| | PostgreSQL | SQLite | InMemory |
|---|---|---|---|
| Persistence | Yes | Yes | No |
| Infrastructure | Database server | None (file) | None |
| Multi-server scheduling | Yes | No | No |
| Full scheduler trains | Yes | Yes | No (simplified) |
| Batch operations | Yes | Yes | No |

## InMemory

```bash
dotnet add package Trax.Effect.Data.InMemory
```

```csharp compile
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
    )
);
```

Uses Entity Framework Core's in-memory provider: no connection string, no migrations. Data is lost when the process exits. `IDataContext` is registered as scoped, and every scope reads the same store, so a test can run a train and then read its row:

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

var services = new ServiceCollection();
services.AddLogging();
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UseInMemory()
    )
    .AddMediator(assembly)
);

await using var provider = services.BuildServiceProvider();

// Run a train, then read its row.
await using var scope = provider.CreateAsyncScope();
var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
var metadata = await context.Metadatas.OrderByDescending(m => m.Id).FirstOrDefaultAsync();
```

## DataContext Logging

Stores the host's log lines in the `trax.log` table: every `ILogger` category at or above the minimum level (`Information` by default) that is not blacklisted, whatever wrote it. It does not record SQL: EF Core's command log (`Microsoft.EntityFrameworkCore.Database.Command`) is always skipped, because writing a row would log another one. Lines are queued in memory (4096, oldest dropped when full) and written in batches. [Debug across processes with the log table](/docs/effect/debug-across-processes-with-the-log-table) reads them.

```csharp compile
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDataContextLogging(minimumLogLevel: LogLevel.Information)
    )
);
```

Log levels can also be changed at runtime via the Dashboard's Server Settings page. A saved level is applied to the host's logger filters over every configuration source, and lasts until the process restarts; if the host sets a category's filter itself after the dashboard, the page reports that the saved level is not in force. The optional `blacklist` names categories not to store, each an exact category name or a pattern in which `*` matches any run of characters:

```csharp compile
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;

effects.UsePostgres(connectionString)
    .AddDataContextLogging(
        minimumLogLevel: LogLevel.Debug,
        blacklist: ["Microsoft.EntityFrameworkCore.Infrastructure.*"]
    );
```

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddDataContextLogging](/docs/sdk-reference/configuration/add-data-context-logging)
