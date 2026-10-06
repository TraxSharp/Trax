---
authors: [Theauxm]
areas: [scheduling]
status: accepted
---

# A scheduled run's bookkeeping lives in the junction that ran it

`JobRunnerTrain` records a scheduled run's success on its manifest inside
`RunScheduledTrainJunction`, straight after the train returns, and saves it on an uncancellable
token. That covers `LastSuccessfulRun`, `NextScheduledRun` and the `Once` auto-disable. They
used to be two junctions of their own, `UpdateManifestSuccessJunction` and
`SaveDatabaseChangesJunction`, and they must not be split out again.

## Status

**Accepted.**

## Why this is written down

Because the split looks like the better design, and the docs once gave its reason: a save
that is its own junction gets its own timing in junction metadata. Someone will want that
back.

The cost is invisible in the chain. A train checks its token before every junction. When a
host shutdown lands after the scheduled train has completed but before the next junction
starts, that check throws. The manifest update and its save never run, and a run that did its
work leaves `LastSuccessfulRun` stale, `NextScheduledRun` uncomputed and a `Once` manifest
enabled to run again. Making only the save uncancellable does not help, because the check that
skips it happens before the save is ever reached.

Folding the update into the junction that ran the train is the only placement the check cannot
reach. Once the train has returned, what follows is bookkeeping for finished work. effect/0005
applies the same rule to a train's own outcome, and the local worker's job-row delete follows
it too.

## Considered options

**Keep the junctions and make their writes uncancellable.** Tried, and the test still fails:
the check before the next junction throws first.

**Run the JobRunner's chain on a token that detaches once the scheduled train returns.** This
keeps the junctions, but the token is the train's and every junction reads it, so it needs a
second token source threaded through the train. It is more machinery to protect the chain's
shape than the shape is worth.

## Consequences

**The save no longer has its own junction timing.** Its time is part of
`RunScheduledTrainJunction`'s.

## Exemplars

- `JobRunnerTrainTests` includes a scheduled train that cancels its JobRunner's token as it
  completes, and asserts the manifest still records the success and a `Once` manifest is
  disabled.
- [JobRunner](/docs/scheduler/admin-trains/job-runner) is the chain this produces.

Not covered: nothing stops a new junction being added after `RunScheduledTrainJunction` that
does bookkeeping. The test only covers the manifest update.

## Changelog

- **2026-09-27**: Recorded.
