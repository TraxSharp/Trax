---
layout: default
title: EffectJunction
description: Reference for EffectJunction, a Junction that records JunctionMetadata and lets junction effects run around it inside a ServiceTrain.
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 4
---

# EffectJunction

A [Junction](/docs/sdk-reference/trains-and-junctions/junction) that records a `JunctionMetadata` for each run and lets junction effects (the [junction logger](/docs/sdk-reference/configuration/add-junction-logger), [junction progress](/docs/sdk-reference/configuration/add-junction-progress), or your own [IJunctionEffectProvider](/docs/sdk-reference/configuration/i-junction-effect-provider)) run before and after it. You write it exactly as you would a `Junction`.

It works only inside a [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train). Chained into a plain `Train`, it throws `TrainException`.

## Signature

```csharp
namespace Trax.Effect.Services.EffectJunction;

public abstract class EffectJunction<TIn, TOut> : Junction<TIn, TOut>
{
    protected EffectJunction();

    public JunctionMetadata? Metadata { get; }

    public abstract override Task<TOut> Run(TIn input);
}
```

It also overrides `RailwayJunction`, and adds a `RailwayJunction` overload that takes a `ServiceTrain`. The train calls them; you do not.

## Members

| Member | Description |
|--------|-------------|
| `Run(TIn input)` | Your implementation, as on `Junction`. |
| `Metadata` | The in-memory record of the current or most recent run of this junction: name, input and output types, start and end time, railway state, whether `Run` was called (`HasRan`), and `OutputJson` when the junction logger serializes output. `null` until the junction first runs, replaced at the start of each run. It is not written to the database itself; the junction effects read it, and the junction logger logs it. |
| `CancellationToken` | Inherited from `Junction`. |

## What happens around Run

1. A fresh `Metadata` is created, named after the junction class, with the train's metadata as its parent.
2. Each enabled junction effect's `BeforeJunctionExecution` runs, in registration order.
3. The start time is stamped, and the junction runs on the railway as a `Junction` does. A failed predecessor skips `Run` and leaves `HasRan` false.
4. The end time and railway state are stamped.
5. Each junction effect's `AfterJunctionExecution` runs, whether the junction succeeded, failed or was skipped.

An exception from a junction effect stops the remaining effects and fails the train, except when the railway already carries a failure: `BeforeJunctionExecution` throwing before a skipped junction, or `AfterJunctionExecution` throwing after the junction failed or was skipped, is logged and the failure already on the railway is what the train reports.

## Example

```csharp
using Trax.Effect.Services.EffectJunction;

public class PersistOrder(IOrderStore store) : EffectJunction<PaymentReceipt, OrderResult>
{
    public override async Task<OrderResult> Run(PaymentReceipt receipt)
    {
        var order = await store.Save(receipt, CancellationToken);
        return new OrderResult(order.Id);
    }
}
```

## When to use it

| Use | When |
|-----|------|
| `EffectJunction` | The junction is in a `ServiceTrain` and should appear in junction logs, junction progress, or your own junction effect |
| `Junction` | The junction is used by a plain `Train`, or you do not want it observed |

See [EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction) for the concepts.

## Package

```
dotnet add package Trax.Effect
```
