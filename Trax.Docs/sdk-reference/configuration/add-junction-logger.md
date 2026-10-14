---
title: AddJunctionLogger
description: Reference for AddJunctionLogger, the junction effect that writes each junction's name, timing and railway state to ILogger before and after it runs.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 22
package: Trax.Effect.JunctionProvider.Logging
namespace: Trax.Effect.JunctionProvider.Logging.Extensions
source:
  - Trax.Effect/src/Trax.Effect.JunctionProvider.Logging/Extensions/ServiceExtensions.cs
---

# AddJunctionLogger

Adds the junction logger, a junction effect that writes each junction's metadata (name, start and end times,
duration, input and output types, railway state) to `ILogger` before and after the junction runs. Nothing is written
to the database.

## Signature

```csharp fragment
public static TBuilder AddJunctionLogger<TBuilder>(
    this TBuilder configurationBuilder,
    bool serializeJunctionData = false
)
    where TBuilder : TraxEffectBuilder
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `serializeJunctionData` | `bool` | `false` | Serialize each successful junction's output into its metadata's `OutputJson`, so the after-junction entry carries it. Junction input is never serialized. |

## Returns

`TBuilder`, the builder type it was called on.

## Behavior

Registers the logger's factory with `AddJunctionEffect`, as a toggleable junction effect, and sets the builder's
`SerializeJunctionData`. Entries are written at the [effect log level](/docs/sdk-reference/configuration/set-effect-log-level)
under the category `Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider`.
Only `EffectJunction`s are logged.

With `serializeJunctionData: true`, the output copy follows the train's lifecycle hook output policy:

- none for a train whose output [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters)
  excludes;
- at most `MaxParameterBytes` (1 MiB by default), past which it is `{"_truncated": true, "_maxBytes": N}`;
- `{"_unserializable": true, "_error": "<exception type>"}` when it cannot be serialized. Serializing never fails
  the train.

Members marked `[TraxSensitive]` are written as `{"_redacted": true}`, and the copies a train is run from (a
manifest's `Properties`, a queued entry's `Input`, a background job's `Input`) as `{"_omitted": true}`.

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJunctionLogger(serializeJunctionData: true)
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [Junction Logger](/docs/effect/effect-providers/junction-logger)
- [AddJunctionEffect](/docs/sdk-reference/configuration/add-effect), [SetEffectLogLevel](/docs/sdk-reference/configuration/set-effect-log-level)
