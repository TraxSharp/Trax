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
public static TraxEffectBuilder SetEffectLogLevel(
    this TraxEffectBuilder builder,
    LogLevel logLevel
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `logLevel` | `LogLevel` | Yes | The minimum log level for effect logging (e.g., `LogLevel.Information`, `LogLevel.Warning`) |

## Returns

`TraxEffectBuilder` — for continued fluent chaining.

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
- This controls the log level for the Trax.Core effect system's own logging, not for the data context logging (which is controlled by [AddDataContextLogging]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-data-context-logging.md %})).
