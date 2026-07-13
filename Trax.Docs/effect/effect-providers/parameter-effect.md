---
layout: default
title: Parameter Effect
parent: Effect Providers
grand_parent: Effect
nav_order: 3
---

# Parameter Effect

The parameter effect serializes train inputs and outputs to JSON and stores them on the `Metadata` record. Without this provider, the `Metadata.Input` and `Metadata.Output` columns are null. You'll know a train ran and whether it succeeded, but not what data it processed.

## Registration

```bash
dotnet add package Trax.Effect.Provider.Parameter
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters()
    )
);
```

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
| `MaxParameterBytes` | `int?` | `null` | Hard byte ceiling per serialized parameter. `null` is unbounded. Over-limit payloads abort mid-serialization and store a `{"_truncated": true, ...}` placeholder. |
| `ShouldSaveOutputs` | `Func<string, bool>?` | `null` | Predicate on the canonical train name; return `false` to skip that train's output. |

The configuration is registered as a singleton and can be modified at runtime via the [Dashboard Effects page](/docs/dashboard#effects-page). Changes take effect on the next train execution scope.

## Bounding what gets stored

Parameter serialization is global: enabling it serializes every train. When a process runs a fan-out of trains whose outputs are large (multi-MB fetch results, cached blobs), that turns into large writes to `trax.metadata` on every run, and serializing several of them concurrently can exhaust host memory. Two knobs bound this without turning serialization off everywhere.

**Skip output for known-large trains.** `ExcludeOutput` skips output serialization for named trains while keeping their (usually tiny) inputs:

```csharp
.SaveTrainParameters(configure: cfg =>
{
    cfg.ExcludeOutput<GetEntitiesQuery>();   // by type
    cfg.ExcludeOutput("GetLeadsQuery");      // or by name fragment
})
```

Matching is a substring check against the canonical train name (`Metadata.Name`), so pass the type that appears in that name: the train interface for named routes, or the request/query type for trains dispatched by input type.

**Cap every parameter.** `MaxParameterBytes` is the automatic safety net for the trains you did not predict:

```csharp
.SaveTrainParameters(configure: cfg => cfg.MaxParameterBytes = 1_048_576)
```

A parameter that serializes past the ceiling is aborted before it is fully materialized (the serializer streams through a byte-counting writer and stops the moment the count is exceeded), and a small placeholder is stored instead. This bounds serialization work for collection and object graphs; it does not shrink the train's return value, which is already resident in memory. For a train that genuinely returns tens of MB, prefer `ExcludeOutput` and reduce what the train returns.

## How It Works

The parameter effect only cares about `Metadata` objects and ignores other tracked models. When `SaveChanges` runs:

1. It iterates through every tracked `Metadata` instance.
2. If `SaveInputs` is enabled, it calls `metadata.GetInputObject()`, serializes it to JSON, and assigns it to `metadata.Input`.
3. If `SaveOutputs` is enabled and the train is not excluded (via `ExcludeOutput`/`ShouldSaveOutputs`), it calls `metadata.GetOutputObject()`, serializes it to JSON, and assigns it to `metadata.Output`.

When `MaxParameterBytes` is set, both serializations run through a streaming writer that aborts once the ceiling is crossed and substitutes the placeholder, so a runaway payload never gets fully built.

These fields are then persisted by whatever data provider you have registered (Postgres or InMemory). When you later inspect train executions (through the [Dashboard](/docs/dashboard), direct database queries, or the metadata API), you can see exactly what went in and what came out.

On disposal, the provider clears the input/output object references from metadata to release memory.

## Requires a Data Provider

This effect populates fields on `Metadata`, but it doesn't persist the metadata itself. You need either `UsePostgres` or `UseInMemory` registered alongside it. Without a data provider, the serialized parameters are written to a `Metadata` object that's never saved anywhere.

## When to Use It

- **Production**: When you need to query or debug train executions after the fact. "What input caused this failure?"
- **Audit trails**: The serialized input/output gives you a record of what data each train processed.
- **Dashboard**: The [Dashboard](/docs/dashboard) displays `Input` and `Output` in its metadata detail view. Without this provider, those fields show as empty.

## SDK Reference

> [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters)
