---
title: AddDataContextLogging
description: Reference for AddDataContextLogging, which stores the host's ILogger messages in the trax.log table, with its level and category filters.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 15
package: Trax.Effect.Data
namespace:
  - Trax.Effect.Data.Extensions
  - Trax.Effect.Data.Services.DataContextLoggingProvider
source:
  - Trax.Effect/src/Trax.Effect.Data/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/DataContextLoggingProvider/DataContextLoggingProvider.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/DataContextLoggingProvider/DataContextLogger.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/DataContextLoggingProvider/IDataContextLoggingProviderConfiguration.cs
redirect_from:
  - /docs/sdk-reference/configuration/add-effect-data-context-logging
---

# AddDataContextLogging

Registers an `ILoggerProvider` that stores the host's `ILogger` messages in the `trax.log` table, readable through
`IDataContext.Logs`. It is a sink for application logging, not a trace of the data context: every category at or
above `minimumLogLevel` that is not blacklisted is stored, whatever wrote it, and no SQL or transaction boundary is
recorded.

## Signature

```csharp fragment
public static TraxEffectBuilderWithData AddDataContextLogging(
    this TraxEffectBuilderWithData configurationBuilder,
    LogLevel? minimumLogLevel = null,
    List<string>? blacklist = null
)
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `minimumLogLevel` | `LogLevel?` | `LogLevel.Information` | The lowest level stored |
| `blacklist` | `List<string>?` | empty | Logger categories not to store: an exact category name, or a pattern in which `*` matches any run of characters, such as `"Microsoft.EntityFrameworkCore.*"` |

## Returns

`TraxEffectBuilderWithData`, for chaining.

## Behavior

Extends `TraxEffectBuilderWithData` only, so it comes after a data provider. Called before one, it fails to
compile (CS0619) with `Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDataContextLogging(...).`

- A line logged while a train runs, on the run's own async flow (its junctions, what they await, its lifecycle
  hooks), stores that run's id in `metadata_id`, so `IDataContext.Logs` and the GraphQL `logs(metadataId:)` filter
  return one run's lines. A line logged outside any run stores `0`.
- EF Core's command log, `Microsoft.EntityFrameworkCore.Database.Command`, is always skipped, because writing a row
  would log another one. Listing it in `blacklist` changes nothing.
- Lines are queued, up to 4,096, and written in the background; a full queue drops its oldest line. When the host
  stops, the provider writes the lines already queued, waiting up to five seconds.
- The dashboard's Server Settings page changes the levels at runtime.

### Registered services

| Service | Lifetime | Role |
|---------|----------|------|
| `IDataContextLoggingProviderConfiguration` | Singleton | A `DataContextLoggingProviderConfiguration` holding `MinimumLogLevel` and `Blacklist` |
| `ILoggerProvider` | Singleton | `DataContextLoggingProvider`. Its `CreateLogger(categoryName)` returns a `DataContextLogger` for the category; `DataContextLogger.BeginScope` returns no scope, since scopes are not stored. |

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDataContextLogging(
            minimumLogLevel: LogLevel.Warning,
            blacklist: ["Microsoft.AspNetCore.*"])
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [Debugging with the log table](/docs/effect/debug-across-processes-with-the-log-table)
- [IDataContext](/docs/sdk-reference/configuration/i-data-context), [SetEffectLogLevel](/docs/sdk-reference/configuration/set-effect-log-level)
