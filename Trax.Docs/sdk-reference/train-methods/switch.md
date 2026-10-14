---
title: Switch
description: "Reference for Switch, which sends the train down one of several declared tracks, one per enum member, chosen by a decision."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Decisions
source:
  - Trax.Core/src/Trax.Core/Monad/Monad.Decisions.cs
  - Trax.Core/src/Trax.Core/Decisions/Tracks.cs
  - Trax.Core/src/Trax.Core/Decisions/Decisions.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 11
---

# Switch

Sends the train down one of several declared tracks, one per member of an enum, chosen by a
decision. Every track is verified at startup; only the chosen one runs.

## Signatures

```csharp fragment
// Train<TInput, TReturn>; MonadTask<TInput, TReturn> and Monad<TInput, TReturn> have the same, public

// Asks the decider which member of TTrack applies to the TState in Memory, then routes
protected MonadTask<TInput, TReturn> Switch<TState, TTrack>(
    Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks,
    string? asking = null
)
    where TTrack : struct, Enum;

// Routes on a ChoiceDecision<TTrack> an earlier Decide put in Memory; asks nothing
protected MonadTask<TInput, TReturn> Switch<TTrack>(
    Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks
)
    where TTrack : struct, Enum;
```

## Parameters

| Parameter | Description |
|---|---|
| `TState` | The value in Memory the question is about (asking form) |
| `TTrack` | The enum whose members name the tracks |
| `tracks` | Declares the tracks on the `Tracks<TInput, TReturn, TTrack>` it is handed |
| `asking` | The question. Defaults to the `[Asks]` attribute on `TTrack`. |

### Tracks\<TInput, TReturn, TTrack\>

| Method | Description |
|---|---|
| `When(TTrack track, Func<MonadTask, MonadTask> then, string? description = null, double? requireConfidence = null)` | The track for `track`. `description` is offered to the decider, defaulting to the member's `[Description]`. `requireConfidence` overrides `RequireConfidence` for this track. |
| `Otherwise(Func<MonadTask, MonadTask> then)` | Where the train goes when the choice has no track here, or its confidence is below its track's bar. Without it, either fails the run. |
| `RequireConfidence(double minimum)` | The bar, from 0 to 1, for every track that sets none. Defaults to 0. |
| `Shadow<TDecider>()` | Asking form only. Also puts the question to `TDecider` and records whether its answer would have taken the same track, by this switch's tracks and bars. Never acted on. See [Decide](/docs/sdk-reference/train-methods/decide). |
| `WaitForShadows(TimeSpan wait)` | Asking form only. How long to wait for the shadows once the live answer is in. Defaults to five seconds. |

A track is written on the parameter it is handed, and may be empty (`t => t`).

## Returns

`MonadTask<TInput, TReturn>`, the next link of the chain.

## Behavior

1. With the asking form, asks as [Decide](/docs/sdk-reference/train-methods/decide) does, offering only the members this switch has tracks for, in declared order.
2. Finds the track for the chosen member, and its bar (`requireConfidence`, else `RequireConfidence`).
3. Takes that track when the choice has one and meets its bar; otherwise takes `Otherwise`, or fails the run (`Permanent`) when there is none.
4. Puts `TrackTaken<TTrack>` in Memory, awaits the `IDecisionObserver`'s `Routed` with a `TrackRouted`, and runs the track, unless the run was cancelled meanwhile.

| Recorded | Members |
|---|---|
| `TrackTaken<TKey>` (in Memory) | `Track`: the track's name (an enum member, or `Otherwise`). `FallbackReason`: why the decision was not followed, or `null`. `TookFallback`: `FallbackReason` is not `null`. |
| `TrackRouted` (to the observer) | `Train`, `RunId`, `On` (the enum routed on), `Track`, `FallbackReason` |

After the switch, the chain can rely only on what every track produces.

| Condition | Result |
|---|---|
| No tracks, a track declared twice, or a track on a value the enum does not define (`When((Lane)7, ...)`) | Refused at startup and at run time. The asking form checks its tracks before it asks, so a refused declaration never costs a decision. |
| Shadows declared on the form that asks nothing | Refused: it routes on a decision made earlier, so shadow the `Decide` that made it |

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class UnderwriteTrain : ServiceTrain<LoanApplication, LoanOutcome>
{
    protected override Task<Either<Exception, LoanOutcome>> Junctions() =>
        Switch<LoanApplication, Underwriting>(tracks => tracks
                .When(Underwriting.Approve, t => t.Chain<MakeOffer>(), requireConfidence: 0.95)
                .When(Underwriting.Decline, t => t.Chain<SendAdverseActionNotice>(), requireConfidence: 0.95)
                .When(Underwriting.ManualReview, t => t.Chain<AssignUnderwriter>()))
            .Resolve();
}
```

## See also

- [Decide](/docs/sdk-reference/train-methods/decide), [Gate](/docs/sdk-reference/train-methods/gate) and [Scale](/docs/sdk-reference/train-methods/scale)
- [Decisions](/docs/core/decisions), including what happens [when the answer is not followed](/docs/core/decisions#when-the-answer-is-not-followed)
