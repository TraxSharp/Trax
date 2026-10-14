---
title: Check the run is still live before a side effect
description: "Re-read a run's own metadata row from inside a junction, by TrainMetadataId, and refuse an irreversible side effect when the run was already failed."
type: how-to
area: effects
nav_order: 11
source:
  - Trax.Effect/src/Trax.Effect/Models/JunctionMetadata/JunctionMetadata.cs
  - Trax.Effect/src/Trax.Effect/Services/EffectJunction/EffectJunction.cs
---

# Check the run is still live before a side effect

The reaper and startup recovery can mark a slow run `Failed` without telling it, and a run that keeps going after that can have its work overtaken by the next run for the same subject. A junction about to do something it cannot undo (send a payment, patch a customer) re-reads its own run's row first and refuses to act when the run is no longer `InProgress`.

## 1. Make the junction an EffectJunction

`EffectJunction<TIn, TOut>.Metadata` is the junction's `JunctionMetadata`, and its `TrainMetadataId` is the primary key of the run's `trax.metadata` row. A plain `Junction<TIn, TOut>` has neither ([EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction)).

Use `TrainMetadataId`, not `TrainExternalId`: `metadata.external_id` has no unique index, so a dispatch retry leaves several rows sharing one external id (the earlier ones `Failed`), and the column is unindexed, so looking a run up by it scans the largest table Trax writes. `TrainMetadataId` is the same on every execution path (a direct run through `ITrainExecutionService.RunAsync`, a queued run picked up by a local worker, a remote or Lambda run), because each runs under the row it was dispatched with. It is `0` when no data provider is registered, since no row exists.

## 2. Read the state immediately before the side effect

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectJunction;

public class PatchCustomerJunction(IDataContext db, ICustomerApi customers)
    : EffectJunction<PatchCustomer, Unit>
{
    public override async Task<Unit> Run(PatchCustomer input)
    {
        var state = await db.Metadatas
            .Where(m => m.Id == Metadata!.TrainMetadataId)
            .Select(m => m.TrainState)
            .FirstOrDefaultAsync();

        if (state != TrainState.InProgress)
            throw new TrainException("This run was already failed; refusing to send.");

        await customers.SendAsync(input);
        return Unit.Default;
    }
}
```

## 3. Register the junction scoped or transient

Read `Metadata` inside `Run`, never in a constructor or a field initializer. `EffectJunction.Metadata` is assigned once per execution. A junction registered with `AddSingletonTraxJunction` and reached through `IChain` is one object shared by every concurrent run, so whichever run assigned it last is the one the others read. For this check that is wrong in the worst direction: the junction reads a live run's id, concludes its own run is still going, and sends.

## SDK Reference

> [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger)
