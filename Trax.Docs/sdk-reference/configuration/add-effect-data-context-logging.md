---
layout: default
title: AddDataContextLogging
parent: Configuration
grand_parent: SDK Reference
nav_order: 3
---

# AddDataContextLogging

Registers an `ILoggerProvider` that stores the host's `ILogger` messages in the `trax.log` table, readable through `IDataContext.Logs`. It is a sink for application logging, not a trace of the data context: every category at or above `minimumLogLevel` that is not blacklisted is stored, whatever wrote it, and no SQL or transaction boundary is recorded. See [Debugging with the log table](/docs/effect/debugging-with-the-log-table) for querying it.

## Signature

```csharp
public static TraxEffectBuilderWithData AddDataContextLogging(
    this TraxEffectBuilderWithData effectBuilder,
    LogLevel? minimumLogLevel = null,
    List<string>? blacklist = null
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `minimumLogLevel` | `LogLevel?` | No | `LogLevel.Information` | Minimum log level to capture. |
| `blacklist` | `List<string>?` | No | `[]` (empty) | Logger categories not to store: an exact category name, or a pattern in which `*` matches any run of characters (e.g., `["Microsoft.EntityFrameworkCore.*"]`) |

## Returns

`TraxEffectBuilderWithData`, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDataContextLogging(
            minimumLogLevel: LogLevel.Warning,
            blacklist: ["Microsoft.EntityFrameworkCore.Database.Command"])
    )
);
```

## Remarks

- **Requires** a data provider ([UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) or [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect)). This is enforced at compile time. `AddDataContextLogging` is only available on `TraxEffectBuilderWithData`, which is returned by the data provider methods. If you try to call it without a data provider, the code will not compile.
- Registers `DataContextLoggingProvider` as an `ILoggerProvider`.
- EF Core's command log, `Microsoft.EntityFrameworkCore.Database.Command`, is always skipped, because writing a row would log another one. Listing it in `blacklist` changes nothing.
- When the host stops, the provider writes the entries already queued, waiting up to five seconds, before it lets go.
- Log levels can be changed at runtime via the Dashboard's Server Settings page.
