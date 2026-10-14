---
title: Junction Logger
description: "The AddJunctionLogger effect that records JunctionMetadata before and after each junction, the serializeJunctionData option and its EffectJunction requirement."
type: reference
area: effects
nav_order: 5
source:
  - Trax.Effect/src/Trax.Effect.JunctionProvider.Logging
  - Trax.Effect/src/Trax.Effect/Models/JunctionMetadata/JunctionMetadata.cs
---

# Junction Logger

The junction logger runs before and after each junction of a train and logs a structured `JunctionMetadata` entry for it: which junction ran, how long it took, its railway state, and optionally what it returned.

## Registration

```bash
dotnet add package Trax.Effect.JunctionProvider.Logging
```

```csharp compile
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJunctionLogger(serializeJunctionData: true)
    )
);
```

## The `serializeJunctionData` Option

When `serializeJunctionData` is `false` (the default), the junction logger records timing, types, and Railway state but not the actual output values.

When `true`, after each junction completes, the logger serializes the junction's output to JSON and stores it in `JunctionMetadata.OutputJson`. This is useful for debugging since you can see exactly what each junction produced, but it adds serialization overhead per junction.

The output is written with the same logging options as a model's `ToString()`: members marked `[TraxSensitive]` are written as `{"_redacted": true}`, a manifest's `Properties`, a queued entry's `Input` and a background job's `Input` as `{"_omitted": true}`, and a metadata row's `Manifest`, `Parent`, `Children` and `Logs` are left out. A junction that returns queued entries therefore logs them without the inputs they will run with.

The copy follows the same rules as the copy of a train's output that lifecycle hooks receive. For a train whose output [`SaveTrainParameters`](/docs/sdk-reference/configuration/save-train-parameters) excludes (`ExcludeOutput`, `ShouldSaveOutputs`, or `SaveOutputs = false`), junction outputs are not serialized either and `OutputJson` stays `null`. Any other copy is bounded by `MaxParameterBytes` (1 MiB when nothing sets it), and one that would cross it is written as `{"_truncated": true, "_maxBytes": N}`.

The junction has already succeeded when its output is serialized, so an output the serializer cannot represent (a `Type` member, a graph nested deeper than 8 levels, a getter that throws) never fails the train. It is written as `{"_unserializable": true, "_error": "<exception type>"}`, the same placeholder the parameter effect uses.

## How It Works

The junction logger is a **junction effect provider**, not a regular effect provider. It hooks into the `EffectJunction` lifecycle rather than the train-level `Track`/`SaveChanges` cycle.

Before each junction runs, the logger creates a `JunctionMetadata` entry with:

| Field | Description |
|-------|-------------|
| `Name` | Junction class name |
| `TrainName` | Parent train name |
| `TrainExternalId` | Parent train's external GUID |
| `TrainMetadataId` | Primary key of the `trax.metadata` row for the run this junction is executing in. `0` when no data provider persisted the run |
| `InputType` / `OutputType` | The junction's generic type arguments |
| `StartTimeUtc` | When the junction began |

After the junction completes:

| Field | Description |
|-------|-------------|
| `EndTimeUtc` | When the junction finished |
| `State` | Railway state: `Right` (success), `Left` (failure), or `Bottom` |
| `HasRan` | Whether the junction actually executed (skipped on failure track) |
| `OutputJson` | Serialized output (only if `serializeJunctionData: true`) |

The completed `JunctionMetadata` is logged at the configured log level under the log category `Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider`, which is the name to use in a logging filter. The provider type itself is internal.

## Requires EffectJunction

The junction logger runs only around junctions that inherit from `EffectJunction<TIn, TOut>`; a plain `Junction<TIn, TOut>` produces no entry. See [EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction).

## Identifying the run from inside a junction

`TrainMetadataId` is the primary key of the run's row. [Check the run is still live before a side effect](/docs/effect/check-the-run-is-still-live) uses it from inside a junction.

## SDK Reference

> [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger)
