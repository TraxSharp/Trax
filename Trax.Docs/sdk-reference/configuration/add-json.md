---
title: AddJson
description: Reference for AddJson and JsonEffectProvider, the effect that logs a tracked model whenever its JSON changes between saves, without storing anything.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 20
package: Trax.Effect.Provider.Json
namespace:
  - Trax.Effect.Provider.Json.Extensions
  - Trax.Effect.Provider.Json.Services.JsonEffect
source:
  - Trax.Effect/src/Trax.Effect.Provider.Json/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Provider.Json/Services/JsonEffect/JsonEffectProvider.cs
redirect_from:
  - /docs/sdk-reference/configuration/add-json-effect
---

# AddJson

Adds the JSON effect, which logs each model a train tracks (its `Metadata`, and anything else passed to `Track`)
when the model's JSON has changed since the last save. Nothing is stored in the database.

## Signature

```csharp fragment
public static TBuilder AddJson<TBuilder>(
    this TBuilder configurationBuilder
)
    where TBuilder : TraxEffectBuilder
```

## Returns

`TBuilder`, the builder type it was called on.

## Behavior

Registers the JSON effect's factory with `AddEffect`, as a toggleable effect, so the effect registry and the
dashboard's Effects page can turn it off at runtime. Each run gets a `JsonEffectProvider`:

| Member | Description |
|--------|-------------|
| `Track(model)` | Serializes the model and keeps the JSON as its starting state |
| `Update(model)` | Does nothing |
| `SaveChanges(cancellationToken)` | Serializes every tracked model again and logs each one whose JSON changed to `ILogger<JsonEffectProvider>`, as the structured argument `{@Metadata}`, at the [effect log level](/docs/sdk-reference/configuration/set-effect-log-level) |
| `Dispose()` | Drops the tracked models |

A run saves its effects twice, when its first row is written and when its outcome is, so the log shows a model
as it started and as it ended. It cannot attribute a change to a junction. Every tracked model is serialized on
every save, changed or not. The JSON compared is the effect configuration's serializer through
`TraxLogSerialization.ForLogging`, which masks `[TraxSensitive]` members and writes the copies a train is run from as
`{"_omitted": true}`. What the log line shows of the model is up to the logging provider.

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJson()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [JSON Effect](/docs/effect/effect-providers/json-effect)
- [SetEffectLogLevel](/docs/sdk-reference/configuration/set-effect-log-level), [AddEffect](/docs/sdk-reference/configuration/add-effect)
