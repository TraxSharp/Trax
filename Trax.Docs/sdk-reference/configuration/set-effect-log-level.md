---
title: SetEffectLogLevel
description: Reference for SetEffectLogLevel, which sets the log level at which the junction logger and the JSON effect write every entry.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 24
package: Trax.Effect
namespace: Trax.Effect.Extensions
source:
  - Trax.Effect/src/Trax.Effect/Extensions/TraxEffectConfigurationBuilderExtensions.cs
---

# SetEffectLogLevel

Sets the level the logging effects write their entries at. The junction logger
([AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger)) and the JSON effect
([AddJson](/docs/sdk-reference/configuration/add-json)) write every entry at this level; the host's logging
configuration decides whether that level is kept.

## Signature

```csharp fragment
public static class TraxEffectBuilderExtensions
{
    public static TBuilder SetEffectLogLevel<TBuilder>(
        this TBuilder builder,
        LogLevel logLevel
    )
        where TBuilder : TraxEffectBuilder;
}
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `logLevel` | `LogLevel` | The level the effects' entries are written at |

## Returns

`TBuilder`, the builder type it was called on.

## Behavior

Sets the builder's `LogLevel`, which defaults to `LogLevel.Debug` and becomes `ITraxEffectConfiguration.LogLevel`.
It is the level entries are written at, not a filter: `LogLevel.Warning` writes each junction's metadata, serialized
output included, as a warning. The categories are the providers' type names, so the host's logging configuration can
filter them. It does not affect [AddDataContextLogging](/docs/sdk-reference/configuration/add-data-context-logging),
which has its own minimum level.

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .SetEffectLogLevel(LogLevel.Trace)
        .UsePostgres(connectionString)
        .AddJunctionLogger()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger), [AddJson](/docs/sdk-reference/configuration/add-json)
