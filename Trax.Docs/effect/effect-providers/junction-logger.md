---
layout: default
title: Junction Logger
parent: Effect Providers
grand_parent: Effect
nav_order: 4
---

# Junction Logger

The junction logger fires before and after each junction in a train, logging structured `JunctionMetadata` entries. This gives you per-junction observability: which junction is running, how long it took, what its Railway state was, and optionally what it returned.

## Registration

```bash
dotnet add package Trax.Effect.JunctionProvider.Logging
```

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJunctionLogger(serializeJunctionData: true)
    )
);
```

## The `serializeJunctionData` Option

When `serializeJunctionData` is `false` (the default), the junction logger records timing, types, and Railway state but not the actual output values.

When `true`, after each junction completes, the logger serializes the junction's output to JSON and stores it in `JunctionMetadata.OutputJson`. This is useful for debugging since you can see exactly what each junction produced, but it adds serialization overhead per junction.

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

The completed `JunctionMetadata` is logged at the configured log level via `ILogger<JunctionLoggerProvider>`.

## Identifying the run from inside a junction

`TrainMetadataId` is the primary key of the run's `trax.metadata` row, so a junction can read its own run back without a scan. Prefer it to `TrainExternalId` for that: `metadata.external_id` has no unique index, so a dispatch retry leaves several rows sharing one external id (the earlier ones `Failed`), and the column is unindexed, so looking a run up by it scans the largest table Trax writes.

The value is the same on every execution path, because each one runs under the metadata row it was dispatched with: a direct run through `ITrainExecutionService.RunAsync`, a queued run picked up by a local worker, and a remote or Lambda run. It is `0` when no data provider is registered, since nothing persisted the run and there is no row to point at.

The usual reason to want it is a liveness check before an irreversible side effect. The reaper and startup recovery can mark a slow run `Failed` without telling it, and a run that keeps going after that can have its work overtaken by the next run for the same subject. Re-reading the row immediately before the side effect lets the junction refuse to act:

```csharp
public class PatchCustomerJunction(IDataContext db) : EffectJunction<PatchCustomer, Unit>
{
    public override async Task<Unit> Run(PatchCustomer input)
    {
        var state = await db.Metadatas
            .Where(m => m.Id == Metadata!.TrainMetadataId)
            .Select(m => m.TrainState)
            .FirstOrDefaultAsync();

        if (state != TrainState.InProgress)
            throw new TrainException("This run was already failed; refusing to send.");

        return await SendAsync(input);
    }
}
```

> **Read `Metadata` inside `Run`, and register the junction scoped or transient.** `EffectJunction.Metadata` is assigned once per execution. A junction registered with `AddSingletonTraxJunction` and reached through `IChain` is one object shared by every concurrent run, so whichever run assigned it last is the one the others read. For logging that is merely confusing. For a check like the one above it is wrong in the worst direction: the junction reads a live run's id, concludes the run is still going, and sends.

## Requires EffectJunction

The junction logger only fires on junctions that inherit from `EffectJunction<TIn, TOut>`. If your junctions use the base `Junction<TIn, TOut>`, the logger has nothing to hook into and won't produce any output.

See [Junctions: EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction) for the difference between the two.

## When to Use It

- **Debugging slow trains**: The timing data shows which junction is the bottleneck.
- **Tracing failures**: The Railway state tells you exactly where and why a train switched to the left track.
- **Development**: Pair with `serializeJunctionData: true` to see junction-by-junction data flow through the chain.

## SDK Reference

> [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger)
