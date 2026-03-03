---
layout: default
title: Parameter Effect
parent: Effect Providers
grand_parent: Usage Guide
nav_order: 3
---

# Parameter Effect

The parameter effect serializes train inputs and outputs to JSON and stores them on the `Metadata` record. Without this provider, the `Metadata.Input` and `Metadata.Output` columns are null—you'll know a train ran and whether it succeeded, but not what data it processed.

## Registration

```bash
dotnet add package Trax.Effect.Provider.Parameter
```

```csharp
services.AddTrax.CoreEffects(options =>
    options
        .AddPostgresEffect(connectionString)
        .SaveTrainParameters()
);
```

*SDK Reference: [SaveTrainParameters]({{ site.baseurl }}{% link sdk-reference/configuration/save-train-parameters.md %})*

You can pass custom serialization options:

```csharp
.SaveTrainParameters(new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
})
```

## Configuration

By default, both inputs and outputs are serialized. You can control this with the `configure` parameter:

```csharp
// Save only inputs (skip output serialization)
.SaveTrainParameters(configure: cfg =>
{
    cfg.SaveInputs = true;
    cfg.SaveOutputs = false;
})

// Save only outputs
.SaveTrainParameters(configure: cfg =>
{
    cfg.SaveInputs = false;
    cfg.SaveOutputs = true;
})
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SaveInputs` | `bool` | `true` | Whether to serialize train input parameters to `Metadata.Input` |
| `SaveOutputs` | `bool` | `true` | Whether to serialize train output parameters to `Metadata.Output` |

The configuration is registered as a singleton and can be modified at runtime via the [Dashboard Effects page](../../dashboard.md#effects-page). Changes take effect on the next train execution scope.

## How It Works

The parameter effect only cares about `Metadata` objects—it ignores other tracked models. When `SaveChanges` runs:

1. It iterates through every tracked `Metadata` instance.
2. If `SaveInputs` is enabled, it calls `metadata.GetInputObject()`, serializes it to JSON, and assigns it to `metadata.Input`.
3. If `SaveOutputs` is enabled, it calls `metadata.GetOutputObject()`, serializes it to JSON, and assigns it to `metadata.Output`.

These fields are then persisted by whatever data provider you have registered (Postgres or InMemory). When you later inspect train executions—through the [Dashboard](../../dashboard.md), direct database queries, or the metadata API—you can see exactly what went in and what came out.

On disposal, the provider clears the input/output object references from metadata to release memory.

## Requires a Data Provider

This effect populates fields on `Metadata`, but it doesn't persist the metadata itself. You need either `AddPostgresEffect` or `AddInMemoryEffect` registered alongside it. Without a data provider, the serialized parameters are written to a `Metadata` object that's never saved anywhere.

## When to Use It

- **Production** — When you need to query or debug train executions after the fact. "What input caused this failure?"
- **Audit trails** — The serialized input/output gives you a record of what data each train processed.
- **Dashboard** — The [Dashboard](../../dashboard.md) displays `Input` and `Output` in its metadata detail view. Without this provider, those fields show as empty.
