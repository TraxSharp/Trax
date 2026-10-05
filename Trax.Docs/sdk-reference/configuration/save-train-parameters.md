---
layout: default
title: SaveTrainParameters
description: Reference for SaveTrainParameters, which stores train inputs and outputs as JSON on Metadata, and the ParameterEffectConfiguration that bounds and masks them.
parent: Configuration
grand_parent: SDK Reference
nav_order: 5
---

# SaveTrainParameters

Serializes train input and output parameters to JSON and stores them in the `Metadata.Input` and `Metadata.Output` fields. Enables parameter inspection in the dashboard and database.

The input is written with the row a run writes when it starts, so a run that never finishes (a Lambda timeout, a killed container, a deploy mid-run) still has its input on its `InProgress` row. The output is written with the outcome.

## Signature

```csharp
public static TBuilder SaveTrainParameters<TBuilder>(
    this TBuilder effectBuilder,
    JsonSerializerOptions? jsonSerializerOptions = null,
    Action<ParameterEffectConfiguration>? configure = null
)
    where TBuilder : TraxEffectBuilder
```

The generic type parameter `TBuilder` is inferred by the compiler, so callers just write `.SaveTrainParameters()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `jsonSerializerOptions` | `JsonSerializerOptions?` | No | `TraxJsonSerializationOptions.Default` | Custom System.Text.Json options for parameter serialization |
| `configure` | `Action<ParameterEffectConfiguration>?` | No | `null` | Optional callback to configure which parameters are serialized |

### ParameterEffectConfiguration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SaveInputs` | `bool` | `true` | Whether to serialize train input parameters to `Metadata.Input` |
| `SaveOutputs` | `bool` | `true` | Whether to serialize train output parameters to `Metadata.Output` |
| `MaxParameterBytes` | `int?` | `1048576` (1 MiB) | Hard byte ceiling per serialized parameter (input and output). A payload that serializes past this many UTF-8 bytes is aborted mid-serialization and stored as `{"_truncated": true, "_maxBytes": N}` instead. `null` removes the ceiling. Must be positive when set. |
| `ShouldSaveInputs` | `Func<string, bool>?` | `null` | Predicate receiving the canonical train name (`Metadata.Name`); return `false` to skip serializing that train's input. Also the way to express an opt-in, which a list of exclusions cannot. |
| `ShouldSaveOutputs` | `Func<string, bool>?` | `null` | Predicate receiving the canonical train name (`Metadata.Name`); return `false` to skip serializing that train's output. The escape hatch for cases the `ExcludeOutput` helpers can't express. |

The configuration is registered as a singleton and can also be modified at runtime via the dashboard's Effects page.

#### Per-train opt-out helpers

For the common case (a known set of trains), use the `ExcludeInput` and `ExcludeOutput` helpers instead of a predicate. Each skips serialization for trains whose canonical name contains the given fragment, and returns the configuration for chaining.

| Method | Description |
|--------|-------------|
| `ExcludeInput(string fragment)` | Skip input for trains whose `Metadata.Name` contains `fragment`. |
| `ExcludeInput(Type type)` | Skip input for trains whose name contains `type.FullName`. |
| `ExcludeInput<TTrain>()` | Same as the `Type` overload, using `typeof(TTrain)`. |
| `ExcludeOutput(string fragment)` | Skip output for trains whose `Metadata.Name` contains `fragment`. |
| `ExcludeOutput(Type type)` | Skip output for trains whose name contains `type.FullName`. |
| `ExcludeOutput<TTrain>()` | Same as the `Type` overload, using `typeof(TTrain)`. |

The two sides are independent: excluding a train's input leaves its output alone, and the reverse.

Matching is a substring check against the canonical name, so pass the type that appears in that name: the train interface for named routes, or the request/query type for trains dispatched by input type (e.g. via the MediatR bridge, where `Metadata.Name` is the assembly-qualified request type). `MaxParameterBytes` is the automatic safety net for the trains you did not predict; the exclusion lists are the explicit knob for the ones you did.

#### Saving only some trains' inputs

Exclusions answer "everything except these". When the list worth keeping is the short one, use the predicate instead:

```csharp
cfg.ShouldSaveInputs = name => name.Contains(typeof(IPatchCustomerTrain).FullName!);
```

That serializes the mutation train's input and nothing else, which is the usual shape when inputs carry personal data and only the replayable ones are worth storing. Pair it with [per-train metadata retention](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) to decide how long each of them is kept.

#### Masking sensitive fields

Excluding a train drops its whole input or output. To keep the record but hide one field, mark the member with `[TraxSensitive]` (namespace `Trax.Effect.Attributes`):

```csharp
public record ChargeCustomerInput(
    string CustomerId,
    [TraxSensitive] string CardNumber,
    Address BillingAddress
);

public class Address
{
    public string City { get; set; } = "";

    [TraxSensitive]
    public string Street { get; set; } = "";
}
```

The stored input then reads `{"customerId": "c-1", "cardNumber": {"_redacted": true}, "billingAddress": {"city": "Leeds", "street": {"_redacted": true}}}`. The train runs with the real values; only the stored copy is masked.

| Case | What happens |
|------|--------------|
| A marked member on a nested object, or on each element of a collection | Masked where it sits; the rest of the object is kept |
| A marked member whose value is an object or a collection | The whole value is replaced; nothing under it is written |
| A positional record parameter | Mark the parameter, with or without `property:` |
| `[JsonPropertyName]` on the member | Masked under its JSON name |
| A mark on a base property or an interface member | Applies to the override or implementation |
| A dictionary's keys or values | Not masked: there is no member to mark |
| A member named `Password` with no mark | Not masked. Nothing is masked by name |

The same masking applies to the junction output recorded by `AddJunctionLogger(serializeJunctionData: true)`, and to the output handed to lifecycle hooks when `SaveTrainParameters` is off, so a broadcast or subscription never carries the value either. It does **not** apply to the copy a train is run from: a queued entry's input and a manifest's properties keep the real value, because the train needs it. Those copies are kept out of logs instead: a model's `ToString()`, the JSON effect and the junction logger write each as `{"_omitted": true}` (`TraxLogSerialization.ForLogging(options)` derives the options they use). The GraphQL operations reads that return them, `workQueue.detail` and `manifestDetail`, mask them on the way out the same way, along with any member typed `object` or as a JSON node, whose stored value the input type cannot describe, as do the dashboard's work queue entry and dead letter pages, and `requeueExecution` and the dashboard's **Re-queue** refuse a recorded input that holds a mask (see [Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate)). A masked input cannot be deserialized back into the input type; `TraxRedaction.ContainsRedaction(json)` says whether a stored input or output holds a mask. Why it is opt-in and masked where it is written is recorded in `effect/0010`.

## Returns

`TBuilder`, the same builder type that was passed in, for continued fluent chaining.

## Examples

Basic usage (saves both inputs and outputs):

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters()
    )
);
```

Save only inputs (skip output serialization):

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters(configure: cfg =>
        {
            cfg.SaveInputs = true;
            cfg.SaveOutputs = false;
        })
    )
);
```

Custom JSON options with configuration:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters(
            jsonSerializerOptions: new JsonSerializerOptions { WriteIndented = false },
            configure: cfg => cfg.SaveOutputs = false
        )
    )
);
```

Keep inputs, drop the output of a few known-large trains, and cap everything else at a ceiling of your own:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters(configure: cfg =>
        {
            cfg.MaxParameterBytes = 512 * 1024;   // 512 KiB ceiling for every parameter
            cfg.ExcludeOutput<GetEntitiesQuery>();
            cfg.ExcludeOutput<GetLeadsQuery>();
            cfg.ExcludeOutput("GetPpaDataFromCache");   // string fragments work too
        })
    )
);
```

A train whose output crosses `MaxParameterBytes` stores `{"_truncated": true, "_maxBytes": 524288}` in `Metadata.Output` instead of the full payload. A train matched by `ExcludeOutput` stores nothing for its output, and its input is still serialized unless `ExcludeInput` or `ShouldSaveInputs` also refuses it.

A parameter that cannot be serialized at all stores `{"_unserializable": true, "_error": "JsonException"}` on the same principle, with the exception's type in `_error`. That covers a reference cycle, an unsupported type, a contract `System.Text.Json` rejects (two members with the same `[JsonPropertyName]`, `[JsonInclude]` on a non-public member), and a property getter that throws. Only the exception's type is kept: the messages carry unbounded detail, which is the wrong thing to put in the column a ceiling exists to bound. The run itself is unaffected, because an output that cannot be stored is a recording problem rather than a reason to fail work that already succeeded.

## Remarks

- Requires a data provider to be registered (the serialized parameters are stored in the database via `Metadata`).
- The serialized JSON is stored in `Metadata.Input` (set on train start) and `Metadata.Output` (set on completion).
- Each object is serialized once. A run is saved at its start, around every junction and at its end, and the effect serializes an input or output only when the metadata is given an object it has not serialized yet. The stored input is therefore the input as it was when the run started: a train that mutates its input object afterwards does not change it.
- Useful for debugging failed trains: inspect the exact input that caused the failure.
- The `ParameterEffectConfiguration` singleton is accessible at runtime. The dashboard's Effects page provides a UI to toggle `SaveInputs` and `SaveOutputs` without restarting the application. The per-train exclusions and predicates are set in code and are not editable there; the global toggles are the outer gate, so turning `SaveInputs` off from the dashboard stops every train's input regardless of what the predicate says.
- **Lifecycle hooks receive the output under the same rules as the stored copy.** When the output is stored, `OnCompleted` hooks read that copy, bounded by `MaxParameterBytes`. When it is not, the train serializes a copy for the hooks in memory, without persisting it, and follows the same decision: an output skipped by `ExcludeOutput`, `ShouldSaveOutputs` or `SaveOutputs = false` is not serialized for the hooks either, and they see `Metadata.Output` as `null`. Any copy that is built is bounded: by `MaxParameterBytes`, and when that is `null` by 1 MiB (`DefaultLifecycleHookOutputPolicy.DefaultMaxCopyBytes`), past which the hooks get the `_truncated` placeholder. The same 1 MiB ceiling applies on a host without `SaveTrainParameters()`, and when the effect is switched off from the dashboard. This matters because the broadcaster, GraphQL and SignalR hooks publish the copy to other processes and every subscriber.
- **`MaxParameterBytes` bounds serialization work, not the result object.** It serializes through a streaming writer and aborts the moment the byte count crosses the ceiling, so an oversized collection or object graph is never fully materialized as a string. It does not shrink the train's return value itself, which is already resident in memory. For a train that genuinely returns tens of MB, prefer `ExcludeOutput` (skip serialization entirely) and reduce what the train returns.

## Package

```
dotnet add package Trax.Effect.Provider.Parameter
```
