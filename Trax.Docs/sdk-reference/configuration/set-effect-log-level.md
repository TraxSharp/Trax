---
layout: default
title: SetEffectLogLevel
parent: Configuration
grand_parent: SDK Reference
nav_order: 9
---

# SetEffectLogLevel

Sets the minimum log level for Trax.Core effect logging.

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
| `logLevel` | `LogLevel` | Yes | The minimum log level for effect logging (e.g., `LogLevel.Information`, `LogLevel.Warning`) |

## Returns

`TBuilder`, the same builder type that was passed in, for continued fluent chaining.

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .SetEffectLogLevel(LogLevel.Warning)
        .UsePostgres(connectionString)
    )
);
```

## Remarks

- The default log level is `LogLevel.Debug`.
- This controls the log level for the Trax.Core effect system's own logging, not for the data context logging (which is controlled by [AddDataContextLogging](/docs/sdk-reference/configuration/add-effect-data-context-logging)).
