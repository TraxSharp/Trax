---
layout: default
title: Junction
description: "Reference for Junction, the base class of one unit of work in a chain: its type parameters, members, railway behaviour and an example."
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 3
---

# Junction

The base class of a junction: one unit of work in a train's chain, taking a `TIn` and producing a `TOut`. You implement `Run`; the base class handles the railway. When an earlier junction has failed, `Run` is not called and the failure passes through.

In a [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), derive from [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) instead when the junction should be visible to the junction logger and junction progress.

## Signature

```csharp
namespace Trax.Core.Junction;

public interface IJunction<TIn, TOut>
{
    Task<TOut> Run(TIn input);
}

public abstract class Junction<TIn, TOut> : IJunction<TIn, TOut>
{
    protected Junction();

    public CancellationToken CancellationToken { get; protected set; }

    public abstract Task<TOut> Run(TIn input);
}
```

`IJunction<TIn, TOut>` and `Junction<TIn, TOut>` also carry a public `RailwayJunction` method, and `Junction` a public `Result` property. Both are hidden from completion: the train calls them, you do not.

## Type Parameters

| Type Parameter | Description |
|----------------|-------------|
| `TIn` | The junction's input. The train takes it from Memory by type. A tuple input is assembled from several Memory values. |
| `TOut` | The junction's output. The train stores it in Memory by type for later junctions and for `Resolve()`. Use `Unit` for a junction that produces nothing. |

## Members

| Member | Description |
|--------|-------------|
| `Run(TIn input)` | Your implementation. Return the output, or throw to fail the train. |
| `CancellationToken` | The running train's token, set before `Run` is called. Pass it to anything you await. |

## Behavior

- **A failed predecessor skips `Run`.** The earlier failure is returned unchanged.
- **Cancellation is checked first.** If the train's token is already cancelled, `Run` is not called and the run ends cancelled.
- **An exception becomes the failure.** Whatever `Run` throws is caught and returned as the train's `Left`, with a `TrainExceptionData` (train name, run `ExternalId`, junction name, exception type, message, stack trace) attached in `exception.Data["TrainExceptionData"]`. The exception's own message is not changed. An `OperationCanceledException` thrown while the train's token is cancelled propagates as cancellation instead, with no data attached. See [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception).

## Example

```csharp
using Trax.Core.Junction;

public class ChargePayment(IPaymentGateway gateway) : Junction<ValidatedOrder, PaymentReceipt>
{
    public override async Task<PaymentReceipt> Run(ValidatedOrder order) =>
        await gateway.Charge(order.CustomerId, order.Total, CancellationToken);
}
```

The train builds the junction itself, taking `IPaymentGateway` for its constructor from Memory or, in a `ServiceTrain`, from the container. Use constructor injection; do not put `[Inject]` on a junction's properties (see [Inject](/docs/sdk-reference/attributes/inject)).

## Remarks

- A junction instance is created for each chain step. Keep per-run state in locals, not fields.
- `Junction` does not record anything. `EffectJunction` adds the per-junction metadata the junction effects read.

See [Junctions](/docs/core/trains-and-junctions#junctions) for the concepts.

## Package

```
dotnet add package Trax.Core
```
