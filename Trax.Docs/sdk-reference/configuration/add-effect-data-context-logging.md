---
layout: default
title: AddDataContextLogging
parent: Configuration
grand_parent: SDK Reference
nav_order: 3
---

# AddDataContextLogging

Enables logging for database operations. Captures SQL queries, transaction boundaries, and errors into the Trax.Core logging pipeline.

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
| `blacklist` | `List<string>?` | No | `[]` (empty) | Namespace patterns to exclude from logging (e.g., `["Microsoft.EntityFrameworkCore.*"]`) |

## Returns

`TraxEffectBuilderWithData` — for continued fluent chaining.

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

- **Requires** a data provider ([UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) or [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect)). This is enforced at compile time — `AddDataContextLogging` is only available on `TraxEffectBuilderWithData`, which is returned by the data provider methods. If you try to call it without a data provider, the code will not compile.
- Registers `DataContextLoggingProvider` as an `ILoggerProvider`.
- Log levels can be changed at runtime via the Dashboard's Server Settings page.
