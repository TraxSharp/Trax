---
layout: default
title: SetEffectLogLevel
description: Reference for SetEffectLogLevel, which sets the log level at which the junction logger and JSON effect write every entry.
parent: Configuration
grand_parent: SDK Reference
nav_order: 9
---

# SetEffectLogLevel

Sets the level at which the logging effects write their entries. It is not a filter: the junction logger (`AddJunctionLogger`) and the JSON effect (`AddJson`) write every entry at this level, and your logging configuration decides whether that level is kept.

## Signature

```csharp
public static TBuilder SetEffectLogLevel<TBuilder>(
    this TBuilder builder,
    LogLevel logLevel
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler, so callers just write `.SetEffectLogLevel(...)`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `logLevel` | `LogLevel` | Yes | The level the effect log entries are written at (e.g., `LogLevel.Debug`, `LogLevel.Trace`) |

## Returns

`TBuilder`, the same builder type that was passed in, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .SetEffectLogLevel(LogLevel.Trace)
        .UsePostgres(connectionString)
    )
);
```

## Remarks

- The default level is `LogLevel.Debug`.
- Raising it raises the volume. `LogLevel.Warning` would write each junction's metadata, serialized output included, as a warning, which most production sinks keep. To quiet the effects, leave the level low and filter it in your logging configuration (the categories are the providers' type names), or lower it to `LogLevel.Trace`.
- It applies to the junction logger and the JSON effect, not to the data context logging (which is controlled by [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging)).
