---
title: SaveTrainParameters
description: Reference for SaveTrainParameters, which stores train inputs and outputs as JSON on Metadata, and the ParameterEffectConfiguration that bounds and masks them.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 21
package: Trax.Effect.Provider.Parameter
namespace:
  - Trax.Effect.Provider.Parameter.Extensions
  - Trax.Effect.Provider.Parameter.Configuration
  - Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory
source:
  - Trax.Effect/src/Trax.Effect.Provider.Parameter/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.Provider.Parameter/Configuration/ParameterEffectConfiguration.cs
---

# SaveTrainParameters

Adds the parameter effect, which serializes each run's input and output to JSON into `Metadata.Input` and
`Metadata.Output`, where the data provider stores them with the run's row and the dashboard and API read them.

## Signature

```csharp fragment
public static TBuilder SaveTrainParameters<TBuilder>(
    this TBuilder builder,
    JsonSerializerOptions? jsonSerializerOptions = null,
    Action<ParameterEffectConfiguration>? configure = null
)
    where TBuilder : TraxEffectBuilder
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `jsonSerializerOptions` | `JsonSerializerOptions?` | `TraxJsonSerializationOptions.Default` | The options inputs and outputs are serialized with. They become the builder's `TrainParameterJsonSerializerOptions`. |
| `configure` | `Action<ParameterEffectConfiguration>?` | `null` | Sets what is serialized, and the ceiling |

### ParameterEffectConfiguration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SaveInputs` | `bool` | `true` | Serialize inputs to `Metadata.Input` |
| `SaveOutputs` | `bool` | `true` | Serialize outputs to `Metadata.Output` |
| `MaxParameterBytes` | `int?` | `1048576` (1 MiB) | The most UTF-8 bytes one serialized input or output may take. A payload past it is abandoned mid-serialization and stored as `{"_truncated": true, "_maxBytes": N}`. `null` removes the ceiling. Zero or less throws `ArgumentOutOfRangeException`; the property carries `[Range(1, int.MaxValue)]`, so the dashboard's Configure Effect dialog refuses it too. |
| `ShouldSaveInputs` | `Func<string, bool>?` | `null` | Called with the canonical train name (`Metadata.Name`); `false` skips that train's input |
| `ShouldSaveOutputs` | `Func<string, bool>?` | `null` | The same, for outputs |

| Method | Description |
|--------|-------------|
| `ExcludeInput(string trainNameFragment)` | Skip the input of trains whose `Metadata.Name` contains the fragment |
| `ExcludeInput(Type type)` | Skip the input of trains whose name contains `type.FullName` |
| `ExcludeInput<TTrain>()` | `ExcludeInput(typeof(TTrain))` |
| `ExcludeOutput(string trainNameFragment)`, `ExcludeOutput(Type type)`, `ExcludeOutput<TTrain>()` | The same, for outputs |

Each method returns the configuration, for chaining. Input and output are decided apart: excluding one leaves the
other. Matching is a substring check against the canonical name: the train interface's full name for a named
route, or the input type's full name for a train dispatched by input type.

## Returns

`TBuilder`, the builder type it was called on.

## Behavior

The input is written with the row a run writes when it starts, so a run that never finishes (a Lambda timeout, a
killed container, a deploy mid-run) still has its input on its `InProgress` row. The output is written with the
outcome. Each object is serialized once: the effect serializes when the metadata is given an object it has not
serialized yet, so the stored input is the input as it was when the run started.

- `MaxParameterBytes` bounds serialization work: the streaming writer stops when the byte count crosses it, so an
  oversized graph is never materialized as a string. It does not shrink the train's return value.
- A parameter that cannot be serialized (a reference cycle, an unsupported type, a contract `System.Text.Json`
  rejects, a getter that throws) is stored as `{"_unserializable": true, "_error": "JsonException"}`, with the
  exception's type in `_error` and never its message. The run's outcome is unaffected.
- The trains behind a state machine's operations (the typed `machines` operations and the generic `stateMachine`
  mutations) store no output, whatever the configuration says, because their output is an instance's snapshot.
  Their input is kept, masked as below. The generic `saveSnapshot`'s input marks its `snapshot` `[TraxSensitive]`,
  so such a run cannot be requeued.
- The `ParameterEffectConfiguration` is a singleton, and the dashboard's Effects page toggles `SaveInputs` and
  `SaveOutputs` at runtime. Those two are the outer gate: turned off, they stop every train whatever the predicates
  and exclusions say. The predicates and exclusions are set in code only.

### Masking sensitive fields

A member marked `[TraxSensitive]` (namespace `Trax.Effect.Attributes`) is stored as `{"_redacted": true}`. The train
runs with the real value; only the stored copy is masked.

| Case | Stored |
|------|--------|
| A marked member on a nested object, or on each element of a collection | Masked where it sits; the rest is kept |
| A marked member whose value is an object or a collection | The whole value replaced |
| A positional record parameter | Mark the parameter, with or without `property:` |
| `[JsonPropertyName]` on the member | Masked under its JSON name |
| A mark on a base property or an interface member | Applies to the override or implementation |
| A dictionary's keys or values | Not masked: there is no member to mark |
| A member named `Password` with no mark | Not masked. Nothing is masked by name. |

The same masking applies to the junction output `AddJunctionLogger(serializeJunctionData: true)` records, and to the
output handed to lifecycle hooks. It does not apply to the copies a train is run from: a queued entry's input and a
manifest's properties keep the real value. A model's `ToString()`, the JSON effect and the junction logger write
those copies as `{"_omitted": true}` (`TraxLogSerialization.ForLogging(options)` derives the options they use), and
the GraphQL `workQueue.detail` and `manifestDetail` reads mask them on the way out, along with any member typed
`object` or as a JSON node. `requeueExecution` and the dashboard's Re-queue refuse a recorded input that holds a
mask, because it cannot be deserialized back into the input type. `TraxRedaction.ContainsRedaction(json)` says
whether a stored value holds one. See
[Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate).

### The output lifecycle hooks receive

`SaveTrainParameters` registers `ParameterLifecycleHookOutputPolicy` as the `ILifecycleHookOutputPolicy`, so
`OnCompleted` hooks get the output under the stored copy's rules. When the output is stored, hooks read that copy.
When it is not, the train serializes one in memory for them, without persisting it, and skips it for an output
`ExcludeOutput`, `ShouldSaveOutputs` or `SaveOutputs = false` refuses, where hooks see `Metadata.Output` as `null`.
A copy is bounded by `MaxParameterBytes`, or by 1 MiB (`DefaultLifecycleHookOutputPolicy.DefaultMaxCopyBytes`)
when that is `null`. The same 1 MiB applies on a host without `SaveTrainParameters()`, and while the effect is
switched off from the dashboard. The broadcaster, GraphQL and SignalR hooks publish this copy.

### Serialization members

These are in `Trax.Effect`, namespaces `Trax.Effect.Utils` and `Trax.Effect.Services.LifecycleHookOutputPolicy`.

| Member | Description |
|--------|-------------|
| `TraxRedaction.WithRedaction(options)` | Options that serialize as `options` do, except that `[TraxSensitive]` members are written as `{"_redacted": true}`. For writing only: reading a mask back throws `JsonException` |
| `TraxRedaction.MarkerProperty` | `"_redacted"`, the property of the object that stands in for a masked value |
| `TraxLogSerialization.OmittedProperty` | `"_omitted"`, the property of the object that stands in for an omitted run-from copy |
| `TraxJsonSerializationOptions.JunctionLogging` | The options the junction logger serializes a junction's output with when `serializeJunctionData` is on, wrapped by `TraxLogSerialization.ForLogging` |
| `ILifecycleHookOutputPolicy.MaxCopyBytes(trainName)` | The ceiling, in UTF-8 bytes, on the output copy built for lifecycle hooks, or `null` to build none |
| `DefaultLifecycleHookOutputPolicy.MaxCopyBytes(trainName)` | `DefaultMaxCopyBytes` for every train |
| `TraxBoundedJson` | Serializes a value under a byte ceiling. Hidden from IntelliSense |
| `TraxBoundedJson.TruncatedPlaceholder(maxBytes)` | `{"_truncated": true, "_maxBytes": N}`, written in place of a value past the ceiling |

### Registered services

| Service | Lifetime | Role |
|---------|----------|------|
| `ParameterEffectConfiguration` | Singleton | The configuration `configure` set |
| `ILifecycleHookOutputPolicy` | Singleton | `ParameterLifecycleHookOutputPolicy` |
| `ParameterEffectProviderFactory` | Singleton | A toggleable effect. Its `Configuration` is the singleton above, and each run gets a `ParameterEffect` from `Create()`. |

## Examples

Inputs and outputs:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

Inputs only, with serializer options of your own:

```csharp compile
using System.Text.Json;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters(
            jsonSerializerOptions: new JsonSerializerOptions { WriteIndented = false },
            configure: cfg => cfg.SaveOutputs = false
        )
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

A lower ceiling, and two trains' outputs left out:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters(configure: cfg =>
        {
            cfg.MaxParameterBytes = 512 * 1024;
            cfg.ExcludeOutput<IRunAuditTrain>();
            cfg.ExcludeOutput("ShipOrder");
        })
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

One train's input kept and every other one's dropped:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters(configure: cfg =>
            cfg.ShouldSaveInputs = name => name.Contains(typeof(IShipOrderTrain).FullName!))
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

A field masked in the stored input:

```csharp compile
using Trax.Effect.Attributes;

public record ChargeCustomerInput(string CustomerId, [TraxSensitive] string CardNumber, Address BillingAddress);

public class Address
{
    public string City { get; set; } = "";

    [TraxSensitive]
    public string Street { get; set; } = "";
}
```

It is stored as `{"customerId": "c-1", "cardNumber": {"_redacted": true}, "billingAddress": {"city": "Leeds", "street": {"_redacted": true}}}`.

## See also

- [Parameter Effect](/docs/effect/effect-providers/parameter-effect)
- [Metadata retention per train](/docs/sdk-reference/scheduler-api/add-metadata-cleanup), for how long stored inputs are kept
- [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook), [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger)
