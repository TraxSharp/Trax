---
title: "5.3 The tracks"
description: "Chapter 5 of the Trax book, section 3: The tracks."
type: tutorial
area: general
parent: "5. Does this order look like fraud"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/does-this-order-look-like-fraud/the-question-and-what-it-is-asked-about
---

# 5.3 The tracks

A held order stops with a message for the customer. The junction that does it has nothing to
return, so it returns `Unit`, the type for "no value".

`Trains/PlaceOrder/Junctions/RefuseOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/RefuseOrderJunction.cs
using Shopfront.Fraud;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class RefuseOrderJunction(ILogger<RefuseOrderJunction> logger)
    : EffectJunction<OrderRisk, Unit>
{
    public override Task<Unit> Run(OrderRisk risk)
    {
        logger.LogWarning("Held a first order of {Total} from {CustomerId}", risk.Total, risk.CustomerId);
        throw new TrainException(
            "We could not take this order without a check. You have not been charged."
        );
    }
}
```

The gate goes after pricing and the risk, and before anything is reserved or charged, so a held
order has nothing to undo.

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

public class PlaceOrderTrain : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>, IPlaceOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<PriceCartJunction>()
            .Chain<AssessRiskJunction>()
            .Gate<OrderRisk, LooksFraudulent>(gate =>
                gate.Yes(hold => hold.Chain<RefuseOrderJunction>())
                    .No(take => take)
                    .Shadow<ICandidateFraudDecider>()
                    .WaitForShadows(TimeSpan.FromSeconds(1))
            )
            .Chain<ReserveStockJunction>()
            .Chain<TakePaymentJunction>()
            .Chain<ConfirmOrderJunction>()
            .Resolve();
}
```

`Gate<OrderRisk, LooksFraudulent>` asks the decider how likely the answer is yes, about the
`OrderRisk` in Memory. At 0.5 or more the train takes the `Yes` track, which refuses the order;
below it, the `No` track, which adds nothing and carries on to `ReserveStockJunction`. Both tracks
are part of the chain, so the startup check that refused a misordered chain in chapter 1 checks
both: whichever is taken, `ReserveStockJunction` still finds its `PricedCart`.

The last two lines are the shadow, which comes later in the chapter.

## A rule decides

The rule the shop uses is policy, not judgement: hold any first order of 100 or more. Policy is
written as a `RuleDecider`, which maps the value asked about to an answer and is always certain of
it.

`Fraud/FraudRules.cs`:

```csharp compile=app file=Fraud/FraudRules.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

public static class FraudRules
{
    // A stolen card's first use is usually its largest: hold first orders of 100 or more.
    public static RuleDecider Live() =>
        new RuleDecider().YesNo<OrderRisk, LooksFraudulent>(risk =>
            risk.FirstOrder && risk.Total >= 100m
        );
}
```

## The rule we might move to

Someone thinks 100 is too high and wants to hold first orders from 60. Switching the live rule
would show what it catches only by stopping real customers. A **shadow** decider is asked every
question the live one is, beside it, and its answer is recorded with whether it agreed. It is
never acted on: a shadow that fails, disagrees or is slow changes nothing about the order.

A shadow is named by type, so the candidate gets a type of its own.

`Fraud/ICandidateFraudDecider.cs`:

```csharp compile=app file=Fraud/ICandidateFraudDecider.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

public interface ICandidateFraudDecider : IDecider;
```

`Fraud/StricterFraudRules.cs`:

```csharp compile=app file=Fraud/StricterFraudRules.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

// Holds first orders from 60. Asked beside the live rule, never acted on.
public sealed class StricterFraudRules : ICandidateFraudDecider
{
    private readonly RuleDecider rules = new RuleDecider().YesNo<OrderRisk, LooksFraudulent>(risk =>
        risk.FirstOrder && risk.Total >= 60m
    );

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken) =>
        rules.Decide(request, cancellationToken);
}
```

`.Shadow<ICandidateFraudDecider>()` on the gate puts every question to it as well.
`.WaitForShadows(TimeSpan.FromSeconds(1))` bounds how long a run waits for it once the live answer
is in. The default is five seconds, which every order would pay for a slow candidate.

## SDK Reference

> [Gate](/docs/sdk-reference/train-methods/gate) | [Decide](/docs/sdk-reference/train-methods/decide) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Write a decider](/docs/core/write-a-decider) | [Try a decider in shadow](/docs/core/try-a-decider-in-shadow) | [Test a train's decisions](/docs/core/test-train-decisions)

---

Next: [5.4 Register Shopfront in one place](/docs/book/does-this-order-look-like-fraud/register-shopfront-in-one-place)
