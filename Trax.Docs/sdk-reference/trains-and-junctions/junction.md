---
title: Junction
description: "Reference for Junction and IJunction, the base class of one unit of work in a chain: type parameters, members, railway behaviour and an example."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Junction
source:
  - Trax.Core/src/Trax.Core/Junction/Junction.cs
  - Trax.Core/src/Trax.Core/Junction/IJunction.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 3
---

# Junction

The base class of a junction: one unit of work in a train's chain, taking a `TIn` and producing a `TOut`. A subclass implements `Run`; the base class handles the railway, so when an earlier junction has failed, `Run` is not called and the failure passes through. [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) derives from it and is what the junction effects of a `ServiceTrain` observe.

## Signature

```csharp fragment
namespace Trax.Core.Junction;

public interface IJunction<TIn, TOut>
{
    Task<TOut> Run(TIn input);

    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    );
}

public abstract class Junction<TIn, TOut> : IJunction<TIn, TOut>
{
    protected Junction();

    public CancellationToken CancellationToken { get; protected set; }

    public abstract Task<TOut> Run(TIn input);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public Either<Exception, TOut> Result { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    );
}
```

## Type parameters

| Type parameter | Description |
|----------------|-------------|
| `TIn` | The junction's input. The train takes it from Memory by type. A tuple input is assembled from several Memory values. |
| `TOut` | The junction's output. The train stores it in Memory by type, for later junctions and for `Resolve()`. A junction that produces nothing returns `Unit`. |

## Members

| Member | Description |
|--------|-------------|
| `Run(TIn input)` | The subclass's work. Returns the output, or throws to fail the train. |
| `CancellationToken` | The running train's token (inside a `Parallel` branch, the branch's), set before `Run` is called. |
| `RailwayJunction` | Called by the train to run the junction on the railway. Hidden from completion. |
| `Result` | The last value `Run` returned. Hidden from completion. |

## Behavior

- **A failed predecessor skips `Run`.** The earlier failure is returned unchanged.
- **Cancellation is checked first.** If the train's token is already cancelled, `Run` is not called and the run ends cancelled.
- **An exception becomes the failure.** Whatever `Run` throws is caught and returned as the train's `Left`, with a `TrainExceptionData` (train name, run `ExternalId`, junction name, exception type, message, stack trace, failure class) in `exception.Data["TrainExceptionData"]`. The exception's own message is not changed. An `OperationCanceledException` thrown while the junction's token is cancelled propagates as cancellation instead, with no data attached. See [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception).
- **One instance per step.** A junction instance is created for each chain step, so per-run state belongs in locals, not fields.
- **Nothing is recorded.** `Junction` writes no metadata; `EffectJunction` adds the per-junction metadata the junction effects read.
- **Dependencies come through the constructor.** The train builds the junction itself from Memory or, in a `ServiceTrain`, the container. `[Inject]` properties on a junction built this way stay `null` (see [Inject](/docs/sdk-reference/attributes/inject)).

## Example

```csharp compile
using Trax.Core.Junction;

public class ChargePayment(IPaymentGateway gateway) : Junction<ValidatedOrder, PaymentReceipt>
{
    public override Task<PaymentReceipt> Run(ValidatedOrder order) =>
        gateway.Charge(order.CustomerId, order.Total, CancellationToken);
}
```

## See also

- [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction), the junction a `ServiceTrain`'s junction effects observe
- [Chain](/docs/sdk-reference/train-methods/chain), which builds and runs a junction
- [Junctions](/docs/core/trains-and-junctions#junctions), how a chain passes values between junctions
