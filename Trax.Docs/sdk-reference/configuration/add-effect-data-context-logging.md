---
layout: default
title: AddEffectDataContextLogging
parent: Configuration
grand_parent: SDK Reference
nav_order: 3
---

# AddEffectDataContextLogging

Enables logging for database operations. Captures SQL queries, transaction boundaries, and errors into the Trax.Core logging pipeline.

## Signature

```csharp
public static Trax.CoreEffectConfigurationBuilder AddEffectDataContextLogging(
    this Trax.CoreEffectConfigurationBuilder configurationBuilder,
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

`Trax.CoreEffectConfigurationBuilder` — for continued fluent chaining.

## Example

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddEffectDataContextLogging(
        minimumLogLevel: LogLevel.Warning,
        blacklist: ["Microsoft.EntityFrameworkCore.Database.Command"])
);
```

## Remarks

- **Must** be called after a data provider ([AddPostgresEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-postgres-effect.md %}) or [AddInMemoryEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %})). Throws an `Exception` if no data provider is registered.
- Registers `DataContextLoggingProvider` as an `ILoggerProvider`.
- Log levels can be changed at runtime via the Dashboard's Server Settings page.
