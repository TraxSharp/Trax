---
title: Scale
description: "Reference for Scale, which routes on where the state falls on the ordered levels of an enum."
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
nav_order: 13
---

# Scale

Routes on where the state falls on an ordered scale: the levels of an enum, in order of their
values, lowest first.

## Signatures

```csharp fragment
// Train<TInput, TReturn>; MonadTask<TInput, TReturn> and Monad<TInput, TReturn> have the same, public

// Asks the decider where the TState in Memory falls, then routes
protected MonadTask<TInput, TReturn> Scale<TState, TLevel>(
    Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale,
    string? asking = null
)
    where TLevel : struct, Enum;

// Routes on a ScoreDecision<TLevel> an earlier Decide put in Memory; asks nothing
protected MonadTask<TInput, TReturn> Scale<TLevel>(
    Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale
)
    where TLevel : struct, Enum;
```

## Parameters

| Parameter | Description |
|---|---|
| `TState` | The value in Memory the question is about (asking form) |
| `TLevel` | The enum whose members are the levels, ordered by their values read as signed numbers |
| `scale` | Declares the tracks on the `ScaleTracks<TInput, TReturn, TLevel>` it is handed |
| `asking` | The question. Defaults to the `[Asks]` attribute on `TLevel`. |

### ScaleTracks\<TInput, TReturn, TLevel\>

| Method | Description |
|---|---|
| `AtLeast(TLevel level, Func<MonadTask, MonadTask> then)` | The track for a score that rounds to `level` or above, up to the next level with a track of its own. The lowest level must have one. |
| `Otherwise(Func<MonadTask, MonadTask> then)` | Where the train goes when the score's confidence is below `RequireConfidence`. Without it, that fails the run. |
| `RequireConfidence(double minimum)` | The bar, from 0 to 1. Defaults to 0. |
| `Shadow<TDecider>()` | Asking form only. Also puts the question to `TDecider` and records whether its answer would have taken the same track, by this scale's bands and bar. Never acted on. See [Decide](/docs/sdk-reference/train-methods/decide). |
| `WaitForShadows(TimeSpan wait)` | Asking form only. How long to wait for the shadows once the live answer is in. Defaults to five seconds. |

## Returns

`MonadTask<TInput, TReturn>`, the next link of the chain.

## Behavior

- With the asking form, asks as [Decide](/docs/sdk-reference/train-methods/decide) does.
- The score rounds to its nearest level, halves up, and the train takes the track declared for the highest level at or below it. With levels `Low`, `Medium`, `High` and tracks on `Low` and `High`, a score of 1.4 rounds to `Medium`, which has no track, and takes `Low`'s; 1.5 rounds to `High`.
- Puts `TrackTaken<TLevel>` in Memory and tells the observer, as [Switch](/docs/sdk-reference/train-methods/switch#behavior) does.
- A score below 0 or above the top level fails the run, classified `Transient`.
- An enum with fewer than two levels is refused, in both forms, as is `AtLeast` on a value the enum does not define.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class TriageTicketTrain : ServiceTrain<Ticket, TriageResult>
{
    protected override Task<Either<Exception, TriageResult>> Junctions() =>
        Scale<Ticket, Urgency>(scale => scale
                .AtLeast(Urgency.Low, t => t.Chain<QueueNormally>())
                .AtLeast(Urgency.High, t => t.Chain<PageOnCall>()))
            .Resolve();
}
```

## See also

- [Decide](/docs/sdk-reference/train-methods/decide), [Switch](/docs/sdk-reference/train-methods/switch) and [Gate](/docs/sdk-reference/train-methods/gate)
- [Decisions](/docs/core/decisions)
