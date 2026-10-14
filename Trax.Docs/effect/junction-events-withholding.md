---
title: How junction events withhold a track
description: "Why marking a question sensitive withholds the path a run took after it, not only the answer, and which facts about a withheld track stay visible."
type: explanation
area: effects
nav_order: 12
source:
  - Trax.Effect/src/Trax.Effect.Data/JunctionEvents
---

# How junction events withhold a track

[Junction events](/docs/effect/junction-events) publish each step of a run: the junctions it ran, the questions it asked and the tracks it took. A question about a type marked [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) has its answer withheld from every event and every `trax.junction_run` row. The reference lists exactly what is left out ([Withholding an answer](/docs/effect/junction-events#withholding-an-answer)); this page is about why it reaches as far as it does.

## The answer is in the path

Withholding only the answer would hide nothing. A routing step sends the run down the track for its answer, and the steps on that track have names: a run that answered "flag for fraud review" goes on to run `OpenFraudCase`, and the next event says so. So once a route's answer is withheld, every step after it is published with its name `(withheld)`, no node id, and no question key, answer or decider for a later question. A node id is withheld with the name because it is built from the route and track the step sits on.

The same reasoning sets the default for steps on an ordinary track. Trax.Core reports where a track starts but not where it rejoins the chain, so every step after a route counts as on its track. A consumer that hides answers from a reader also has to hide the names of steps on a track, since they name the track, which is why the SignalR sink and the GraphQL broadcast view do so unless the host opts in. This errs toward hiding: a step that runs on every track after the rejoin is hidden too.

In `Parallel` branches the same lines hold per branch. A withheld route in one branch withholds that branch's later steps, not a sibling running beside it, whose steps run alongside the track rather than on it. After the join everything is withheld, because the steps after the join follow every branch, the withheld one included.

## What stays visible

Some facts about a withheld track describe the run rather than its data, and stay visible by design:

- how many steps ran after the route, their positions and kinds, and when each started and how long it took, which can differ from track to track
- a junction that failed there: its exception's type and failure class
- the run's own failure, which `metadata.failure_junction` and the train's `Failed` event name as they always have
- a train started from a junction on the track, which is a run of its own, with steps of its own

Hiding these would mean a run whose timeline cannot be drawn, whose failures cannot be triaged and whose child runs disappear. The trade-off is that a track can still be told apart by its shape: a reader who knows that one track runs three junctions and the other runs one can tell which was taken. A host for which that says too much does not turn junction events on for that train, and keeps it off a broadcast feed.

## Why `trax.decision` keeps the answer

The decision record is not a feed. A requeue replays a run's answers from `trax.decision`, so the answer has to be there; the journal's log line writes it as withheld instead. [Decision records](/docs/effect/decisions#keying-the-state-hash) covers how such a question's state hash is stored.

`Trax.Effect/docs/adr/0019` records the rules of junction events and why each is the way it is.
