---
layout: default
title: JSON Effect
parent: Effect Providers
grand_parent: Effect
nav_order: 2
---

# JSON Effect

The JSON effect tracks model changes by comparing JSON snapshots. When a train calls `SaveChanges`, this provider serializes every tracked model to JSON and compares it to the snapshot taken when the model was first tracked. If something changed, it logs the current state.

This is a development debugging tool. It doesn't persist anything. It writes to your configured `ILogger`.

## Registration

```bash
dotnet add package Trax.Effect.Provider.Json
```

```csharp
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

This gives you a log of what changed during train execution without needing a database. When you see a train misbehaving, the JSON effect shows you the model states at the point they were saved.

## When to Use It

- **Development**: See train state changes in your console output without setting up Postgres.
- **Debugging**: When a train produces unexpected results, the JSON logs show exactly what each model looked like at `SaveChanges` time.
- **Lightweight setups**: Pair it with `UseInMemory()` for a no-infrastructure development environment.

In production, you'll typically replace this with (or supplement it by) the [Data Persistence](data-persistence.md) provider.

## SDK Reference

> [AddJson](/docs/sdk-reference/configuration/add-json-effect)
