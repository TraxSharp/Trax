---
layout: default
title: SaveTrainParameters
parent: Configuration
grand_parent: SDK Reference
nav_order: 5
---

# SaveTrainParameters

Serializes train input and output parameters to JSON and stores them in the `Metadata.Input` and `Metadata.Output` fields. Enables parameter inspection in the dashboard and database.

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
| `MaxParameterBytes` | `int?` | `null` | Hard byte ceiling per serialized parameter (input and output). `null` is unbounded. A payload that serializes past this many UTF-8 bytes is aborted mid-serialization and stored as `{"_truncated": true, "_maxBytes": N}` instead. Must be positive. |
| `ShouldSaveOutputs` | `Func<string, bool>?` | `null` | Predicate receiving the canonical train name (`Metadata.Name`); return `false` to skip serializing that train's output. The escape hatch for cases the `ExcludeOutput` helpers can't express. |

The configuration is registered as a singleton and can also be modified at runtime via the dashboard's Effects page.

#### Output opt-out helpers

For the common case (a known set of large fetch trains), use the `ExcludeOutput` helpers instead of a predicate. Each skips output serialization for trains whose canonical name contains the given fragment, and returns the configuration for chaining.

| Method | Description |
|--------|-------------|
| `ExcludeOutput(string fragment)` | Skip output for trains whose `Metadata.Name` contains `fragment`. |
| `ExcludeOutput(Type type)` | Skip output for trains whose name contains `type.FullName`. |
| `ExcludeOutput<TTrain>()` | Same as the `Type` overload, using `typeof(TTrain)`. |

Matching is a substring check against the canonical name, so pass the type that appears in that name: the train interface for named routes, or the request/query type for trains dispatched by input type (e.g. via the MediatR bridge, where `Metadata.Name` is the assembly-qualified request type). `MaxParameterBytes` is the automatic safety net for the trains you did not predict; the `ExcludeOutput` list is the explicit knob for the ones you did.

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

Keep inputs, drop the output of a few known-large trains, and cap everything else:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters(configure: cfg =>
        {
            cfg.MaxParameterBytes = 1_048_576;   // 1 MB ceiling for every parameter
            cfg.ExcludeOutput<GetEntitiesQuery>();
            cfg.ExcludeOutput<GetLeadsQuery>();
            cfg.ExcludeOutput("GetPpaDataFromCache");   // string fragments work too
        })
    )
);
```

A train whose output crosses `MaxParameterBytes` stores `{"_truncated": true, "_maxBytes": 1048576}` in `Metadata.Output` instead of the full payload. A train matched by `ExcludeOutput` stores nothing for its output, while its input is still serialized.

## Remarks

- Requires a data provider to be registered (the serialized parameters are stored in the database via `Metadata`).
- The serialized JSON is stored in `Metadata.Input` (set on train start) and `Metadata.Output` (set on completion).
- Useful for debugging failed trains: inspect the exact input that caused the failure.
- The `ParameterEffectConfiguration` singleton is accessible at runtime. The dashboard's Effects page provides a UI to toggle `SaveInputs` and `SaveOutputs` without restarting the application.
- **Lifecycle hooks always receive serialized output.** Even without `SaveTrainParameters()`, `Metadata.Output` is populated in-memory before lifecycle hooks fire, so hooks like `GraphQLSubscriptionHook` can always include output data in subscription events. However, without `SaveTrainParameters()` the output is **not persisted to the database** and exists only in-memory for the duration of the hook execution. Use `SaveTrainParameters()` when you need output stored in the database for the dashboard, queries, or auditing.
- **`MaxParameterBytes` bounds serialization work, not the result object.** It serializes through a streaming writer and aborts the moment the byte count crosses the ceiling, so an oversized collection or object graph is never fully materialized as a string. It does not shrink the train's return value itself, which is already resident in memory. For a train that genuinely returns tens of MB, prefer `ExcludeOutput` (skip serialization entirely) and reduce what the train returns.

## Package

```
dotnet add package Trax.Effect.Provider.Parameter
```
