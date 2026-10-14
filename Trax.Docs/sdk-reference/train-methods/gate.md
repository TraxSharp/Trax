---
title: Gate
description: "Reference for Gate, which routes on the probability that a yes/no answer is yes, to a Yes, No or Unsure track."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Decisions
source:
  - Trax.Core/src/Trax.Core/Monad/Monad.Decisions.cs
  - Trax.Core/src/Trax.Core/Decisions/Tracks.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 12
---

# Gate

Routes on the probability that the answer to a yes/no question is yes: a Yes track, a No track,
and optionally an Unsure track for the band between them.

## Signatures

```csharp fragment
// Train<TInput, TReturn>; MonadTask<TInput, TReturn> and Monad<TInput, TReturn> have the same, public

// Asks the decider how likely yes is for the TState in Memory, then routes
protected MonadTask<TInput, TReturn> Gate<TState, TQuestion>(
    Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate,
    string? asking = null
);

// Routes on a YesNoDecision<TQuestion> an earlier Decide put in Memory; asks nothing
protected MonadTask<TInput, TReturn> Gate<TQuestion>(
    Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate
);
```

## Parameters

| Parameter | Description |
|---|---|
| `TState` | The value in Memory the question is about (asking form) |
| `TQuestion` | A marker type naming the question, usually carrying `[Asks]`. Memory is keyed by type, so each yes/no question a train asks needs its own. |
| `gate` | Declares the tracks on the `GateTracks<TInput, TReturn>` it is handed |
| `asking` | The question. Defaults to the `[Asks]` attribute on `TQuestion`. |

### GateTracks\<TInput, TReturn\>

| Method | Description |
|---|---|
| `Yes(Func<MonadTask, MonadTask> then, double atLeast = 0.5)` | Taken when the probability of yes is at least `atLeast`. Required. |
| `No(Func<MonadTask, MonadTask> then, double below = 0.5)` | Taken when it is below `below`. Required. |
| `Unsure(Func<MonadTask, MonadTask> then)` | Taken when it is at least `below` and below `atLeast`. Without it, that band fails the run. |
| `Shadow<TDecider>()` | Asking form only. Also puts the question to `TDecider` and records whether its answer would have taken the same track, by this gate's bars. Never acted on. See [Decide](/docs/sdk-reference/train-methods/decide). |
| `WaitForShadows(TimeSpan wait)` | Asking form only. How long to wait for the shadows once the live answer is in. Defaults to five seconds. |

With the defaults there is no band between the bars.

## Returns

`MonadTask<TInput, TReturn>`, the next link of the chain.

## Behavior

- With the asking form, asks as [Decide](/docs/sdk-reference/train-methods/decide) does.
- A yes/no answer has no separate confidence: the probability is the answer, so the bars are on the probability itself.
- Puts `TrackTaken<TQuestion>` in Memory (`Yes`, `No` or `Unsure`) and tells the observer, as [Switch](/docs/sdk-reference/train-methods/switch#behavior) does. An `Unsure` outcome is a declared track, not an overruled decision, so its `TrackTaken` carries no fallback reason.
- A gate missing its Yes or No track, or whose No bar is above its Yes bar, is refused.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class DeliverMessageTrain : ServiceTrain<Message, Delivery>
{
    protected override Task<Either<Exception, Delivery>> Junctions() =>
        Gate<Message, ContainsThreat>(gate => gate
                .Yes(t => t.Chain<Block>(), atLeast: 0.8)
                .No(t => t.Chain<Deliver>(), below: 0.2)
                .Unsure(t => t.Chain<HoldForReview>()))
            .Resolve();
}
```

## See also

- [Decide](/docs/sdk-reference/train-methods/decide), [Switch](/docs/sdk-reference/train-methods/switch) and [Scale](/docs/sdk-reference/train-methods/scale)
- [Decisions](/docs/core/decisions)
