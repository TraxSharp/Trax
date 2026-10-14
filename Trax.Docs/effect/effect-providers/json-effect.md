---
title: JSON Effect
description: The AddJson effect, a development tool that logs tracked model changes on SaveChanges by comparing JSON snapshots, without persisting anything.
type: reference
area: effects
nav_order: 3
source:
  - Trax.Effect/src/Trax.Effect.Provider.Json
---

# JSON Effect

The JSON effect tracks model changes by comparing JSON snapshots. When a train calls `SaveChanges`, this provider serializes every tracked model to JSON and compares it to the snapshot taken when the model was first tracked. If something changed, it logs the current state.

It persists nothing; it writes to the configured `ILogger`. [Data persistence](/docs/effect/effect-providers/data-persistence) is the provider that stores runs.

## Registration

```bash
dotnet add package Trax.Effect.Provider.Json
```

```csharp compile
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJson()
    )
);
```

No configuration required. The provider uses the JSON serialization options from `ITraxEffectConfiguration` and logs at the level configured there.

## How It Works

1. When the `EffectRunner` calls `Track(model)`, the JSON effect serializes the model to a JSON string and stores that snapshot alongside a reference to the model.
2. When `SaveChanges` runs at the end of the train, the provider re-serializes every tracked model and compares the new JSON to the stored snapshot.
3. If the JSON differs (a field was updated, a state changed), the provider logs the new serialized state.

The line is the model's `ToString()`. For a metadata row that is the row itself, without the graph loaded around it: `Manifest`, `Parent`, `Children` and `Logs` are left out, and the run's input is not written (`Metadata.Input` is never serialized). A manifest's `Properties`, a queued entry's `Input` and a background job's `Input` are written as `{"_omitted": true}` wherever they appear, because they are the copy a train is run from and hold its input unmasked. Members marked `[TraxSensitive]` are written as `{"_redacted": true}`.

## SDK Reference

> [AddJson](/docs/sdk-reference/configuration/add-json)
