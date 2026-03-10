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

The generic type parameter `TBuilder` is inferred by the compiler — callers just write `.SaveTrainParameters()`. This preserves the concrete builder type through chaining (e.g., `TraxEffectBuilderWithData` stays as `TraxEffectBuilderWithData`).

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

The configuration is registered as a singleton and can also be modified at runtime via the dashboard's Effects page.

## Returns

`TBuilder` — the same builder type that was passed in, for continued fluent chaining.

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

## Remarks

- Requires a data provider to be registered (the serialized parameters are stored in the database via `Metadata`).
- The serialized JSON is stored in `Metadata.Input` (set on train start) and `Metadata.Output` (set on completion).
- Useful for debugging failed trains — inspect the exact input that caused the failure.
- The `ParameterEffectConfiguration` singleton is accessible at runtime. The dashboard's Effects page provides a UI to toggle `SaveInputs` and `SaveOutputs` without restarting the application.

## Package

```
dotnet add package Trax.Effect.Provider.Parameter
```
