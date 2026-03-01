---
layout: default
title: SaveWorkflowParameters
parent: Configuration
grand_parent: API Reference
nav_order: 5
---

# SaveWorkflowParameters

Serializes workflow input and output parameters to JSON and stores them in the `Metadata.Input` and `Metadata.Output` fields. Enables parameter inspection in the dashboard and database.

## Signature

```csharp
public static Trax.CoreEffectConfigurationBuilder SaveWorkflowParameters(
    this Trax.CoreEffectConfigurationBuilder builder,
    JsonSerializerOptions? jsonSerializerOptions = null,
    Action<ParameterEffectConfiguration>? configure = null
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `jsonSerializerOptions` | `JsonSerializerOptions?` | No | `Trax.CoreJsonSerializationOptions.Default` | Custom System.Text.Json options for parameter serialization |
| `configure` | `Action<ParameterEffectConfiguration>?` | No | `null` | Optional callback to configure which parameters are serialized |

### ParameterEffectConfiguration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SaveInputs` | `bool` | `true` | Whether to serialize workflow input parameters to `Metadata.Input` |
| `SaveOutputs` | `bool` | `true` | Whether to serialize workflow output parameters to `Metadata.Output` |

The configuration is registered as a singleton and can also be modified at runtime via the dashboard's Effects page.

## Returns

`Trax.CoreEffectConfigurationBuilder` — for continued fluent chaining.

## Examples

Basic usage (saves both inputs and outputs):

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .SaveWorkflowParameters()
);
```

Save only inputs (skip output serialization):

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .SaveWorkflowParameters(configure: cfg =>
    {
        cfg.SaveInputs = true;
        cfg.SaveOutputs = false;
    })
);
```

Custom JSON options with configuration:

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .SaveWorkflowParameters(
        jsonSerializerOptions: new JsonSerializerOptions { WriteIndented = false },
        configure: cfg => cfg.SaveOutputs = false
    )
);
```

## Remarks

- Requires a data provider to be registered (the serialized parameters are stored in the database via `Metadata`).
- The serialized JSON is stored in `Metadata.Input` (set on workflow start) and `Metadata.Output` (set on completion).
- Useful for debugging failed workflows — inspect the exact input that caused the failure.
- The `ParameterEffectConfiguration` singleton is accessible at runtime. The dashboard's Effects page provides a UI to toggle `SaveInputs` and `SaveOutputs` without restarting the application.

## Package

```
dotnet add package Trax.Effect.Provider.Parameter
```
