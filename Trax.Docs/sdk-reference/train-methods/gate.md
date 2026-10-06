---
layout: default
title: Gate
description: Reference for Gate, which routes on the probability that a yes/no answer is yes, to a Yes, No or Unsure track.
parent: Train Methods
grand_parent: SDK Reference
nav_order: 12
---

# Gate

Routes on the probability that the answer to a yes/no question is yes: a Yes track, a No track,
and optionally an Unsure track for the band between them. See [Decisions](/docs/core/decisions).

## Gate\<TState, TQuestion\>(gate, asking)

Asks the registered decider how likely yes is for the `TState` in Memory, then routes.

```csharp
protected MonadTask<TInput, TReturn> Gate<TState, TQuestion>(
    Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate,
    string? asking = null
)
```

| Type Parameter | Description |
|---|---|
| `TState` | The value in Memory the question is about |
| `TQuestion` | A marker type naming the question, usually carrying `[Asks]`. Memory is keyed by type, so each yes/no question a train asks needs its own. |

## Gate\<TQuestion\>(gate)

Routes on a `YesNoDecision<TQuestion>` already in Memory. Asks nothing.

## GateTracks\<TInput, TReturn\>

| Method | Description |
|---|---|
| `Yes(Func<MonadTask, MonadTask> then, double atLeast = 0.5)` | Taken when the probability of yes is at least `atLeast`. Required. |
| `No(Func<MonadTask, MonadTask> then, double below = 0.5)` | Taken when it is below `below`. Required. |
| `Unsure(Func<MonadTask, MonadTask> then)` | Taken when it is at least `below` and below `atLeast`. Without it, that band fails the run. |
| `Shadow<TDecider>()` | Asking form only. Also puts the question to `TDecider` and records whether its answer would have taken the same track, by this gate's bars. Never acted on. See [Decide](/docs/sdk-reference/train-methods/decide). |
| `WaitForShadows(TimeSpan wait)` | Asking form only. How long to wait for the shadows once the live answer is in. Defaults to five seconds. |

With the defaults there is no band between the bars.

## Example

```csharp
Gate<Message, ContainsThreat>(gate => gate
        .Yes(t => t.Chain<Block>(), atLeast: 0.8)
        .No(t => t.Chain<Deliver>(), below: 0.2)
        .Unsure(t => t.Chain<HoldForReview>()))
    .Resolve();
```

## Remarks

- A yes/no answer has no separate confidence: the probability is the answer, so a gate's bars are
  on the probability itself.
- A gate missing its Yes or No track, or whose No bar is above its Yes bar, is refused.
- An `Unsure` outcome is a declared track, not an overruled decision, so its `TrackTaken` carries
  no fallback reason.
