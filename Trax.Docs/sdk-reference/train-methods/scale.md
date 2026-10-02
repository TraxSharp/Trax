---
layout: default
title: Scale
description: Reference for Scale, which routes on where the state falls on the ordered levels of an enum.
parent: Train Methods
grand_parent: SDK Reference
nav_order: 13
---

# Scale

Routes on where the state falls on an ordered scale: the levels of an enum, in order of their
values, lowest first. See [Decisions](/docs/core/decisions).

## Scale\<TState, TLevel\>(scale, asking)

Asks the registered decider where the `TState` in Memory falls, then routes.

```csharp
protected MonadTask<TInput, TReturn> Scale<TState, TLevel>(
    Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale,
    string? asking = null
)
    where TLevel : struct, Enum
```

## Scale\<TLevel\>(scale)

Routes on a `ScoreDecision<TLevel>` already in Memory. Asks nothing.

## ScaleTracks\<TInput, TReturn, TLevel\>

| Method | Description |
|---|---|
| `AtLeast(TLevel level, Func<MonadTask, MonadTask> then)` | The track for a score that rounds to `level` or above, up to the next level with a track of its own. The lowest level must have one. |
| `Otherwise(Func<MonadTask, MonadTask> then)` | Where the train goes when the score's confidence is below `RequireConfidence`. Without it, that fails the run. |
| `RequireConfidence(double minimum)` | The bar, from 0 to 1. Defaults to 0. |
| `Shadow<TDecider>()` | Asking form only. Also puts the question to `TDecider` and records whether its answer would have taken the same track, by this scale's bands and bar. Never acted on. See [Decide](/docs/sdk-reference/train-methods/decide). |
| `WaitForShadows(TimeSpan wait)` | Asking form only. How long to wait for the shadows once the live answer is in. Defaults to five seconds. |

The score rounds to its nearest level, halves up, and the train takes the track declared for the
highest level at or below it.

## Example

```csharp
Scale<Ticket, Urgency>(scale => scale
        .AtLeast(Urgency.Low, t => t.Chain<QueueNormally>())
        .AtLeast(Urgency.High, t => t.Chain<PageOnCall>()))
    .Resolve();
```

With levels `Low`, `Medium`, `High`, a score of 1.4 rounds to `Medium`, which has no track, and
takes `Low`'s; 1.5 rounds to `High`.

## Remarks

- A score below 0 or above the top level fails the run, classified `Transient`.
- An enum with fewer than two levels is refused, in both forms, as is `AtLeast` on a value the
  enum does not define.
- Levels are ordered by their values read as signed numbers, so a negative member is lower than
  zero.
