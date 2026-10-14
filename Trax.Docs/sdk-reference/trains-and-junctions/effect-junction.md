---
title: EffectJunction
description: "Reference for EffectJunction, a Junction that records JunctionMetadata and lets junction effects run around it inside a ServiceTrain."
type: reference
area: effects
package: Trax.Effect
namespace: Trax.Effect.Services.EffectJunction
source:
  - Trax.Effect/src/Trax.Effect/Services/EffectJunction/EffectJunction.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 4
---

# EffectJunction

A [Junction](/docs/sdk-reference/trains-and-junctions/junction) that records a `JunctionMetadata` for each run and lets junction effects (the [junction logger](/docs/sdk-reference/configuration/add-junction-logger), [junction progress](/docs/sdk-reference/configuration/add-junction-progress), or an [IJunctionEffectProvider](/docs/sdk-reference/configuration/i-junction-effect-provider) of your own) run before and after it. It is written exactly as a `Junction` is, and runs only inside a [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train).

## Signature

```csharp fragment
namespace Trax.Effect.Services.EffectJunction;

public abstract class EffectJunction<TIn, TOut> : Junction<TIn, TOut>
{
    protected EffectJunction();

    public JunctionMetadata? Metadata { get; }

    public abstract override Task<TOut> Run(TIn input);

    public sealed override Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    );
    public Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain
    );
}
```

## Members

| Member | Description |
|--------|-------------|
| `Run(TIn input)` | The subclass's work, as on `Junction`. |
| `Metadata` | The in-memory record of the current or most recent run of this junction: name, input and output types, start and end time, railway state, whether `Run` was called (`HasRan`), and `OutputJson` when the junction logger serializes output. `null` until the junction first runs, replaced at the start of each run. Not written to the database itself; the junction effects read it, and the junction logger logs it. |
| `RailwayJunction` | Called by the train. The `Train` overload hands over to the `ServiceTrain` one. |
| `CancellationToken` | Inherited from `Junction`. |

## Behavior

Around each run:

1. A fresh `Metadata` is created, named after the junction class, with the train's metadata as its parent.
2. Each enabled junction effect's `BeforeJunctionExecution` runs, in registration order.
3. The start time is stamped, and the junction runs on the railway as a `Junction` does. A failed predecessor skips `Run` and leaves `HasRan` false.
4. When [junction events](/docs/sdk-reference/configuration/add-junction-events) are on, a junction that is not skipped is reported as started and as ended.
5. The end time and railway state are stamped.
6. Each junction effect's `AfterJunctionExecution` runs, whether the junction succeeded, failed or was skipped.

An exception from a junction effect stops the remaining effects and fails the train, except when the railway already carries a failure: `BeforeJunctionExecution` throwing before a skipped junction, or `AfterJunctionExecution` throwing after the junction failed or was skipped, is logged, and the failure already on the railway is what the train reports.

| Condition | Exception |
|-----------|-----------|
| Chained into a plain `Train` | `TrainException`: `Cannot run an EffectJunction (<junction>) against a non-ServiceTrain (<train>)` |
| The `ServiceTrain` has no `Metadata` | `TrainException` |

## Example

```csharp compile
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

## See also

- [EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction), which to derive from
- [Junction](/docs/sdk-reference/trains-and-junctions/junction)
- [IJunctionEffectProvider](/docs/sdk-reference/configuration/i-junction-effect-provider), a junction effect of your own
