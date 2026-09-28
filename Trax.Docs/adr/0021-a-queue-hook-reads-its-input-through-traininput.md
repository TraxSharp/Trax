---
authors: [Theauxm]
repos: [effect, mediator]
areas: [platform]
status: accepted
---

# A queue hook reads its input through TrainInput, not through the train's Metadata

`QueueSubjectKey` and `OnQueue` run at enqueue time on an instance that has not run, so
`TrainInput` returned `default` there. A key written as `$"order:{TrainInput.OrderId}"` was
`"order:0"` for every enqueue and serialized every order behind every other, with nothing to say
so. `TrainInput` now works there: the enqueue path hands the instance the input it already carries
on the hook's `metadata` argument, through `ServiceTrain.EnterQueueHooks(metadata)`, which returns
a scope. The input lives for that scope, on that async flow, for that instance only, and never
becomes the train's `Metadata`.

## Status

**Accepted.**

## Considered options

**Throw from `TrainInput` in the hooks.** Keeps the documented contract and turns the silent
wrong key into a loud one. Rejected by the user in favour of removing the trap: the property reads
naturally in a key, and the input is right there.

**Assign the enqueue's metadata to `Metadata`.** The obvious fix, and wrong. A scoped train
resolved twice in one scope is one instance, so the instance the enqueue touched can be the one a
later run in that scope uses. `Run` initializes metadata only when `Metadata` is null, so that run
would skip initialization and record itself on an unsaved enqueue row. It would also need
`Metadata`'s setter opened, which is `internal` because a train's metadata is the record of its
run.

**`InternalsVisibleTo` for Trax.Mediator.** Reaches the setter without widening the surface, but
still assigns `Metadata`, and ties two separately released packages to each other's internals.

**A field on the instance.** Two enqueues on a shared scope, such as a Blazor circuit, run their
hooks on one scoped instance at the same time, and a field would hand one enqueue's input to the
other. The input is kept in an `AsyncLocal` for that reason, the same way the ambient enqueue
context is.

**A non-generic interface the mediator casts to.** Train discovery registers a train under its
first non-generic interface, so one added to `ServiceTrain` would become every train's service
type. The method is public on `ServiceTrain<TIn, TOut>` and hidden from IntelliSense instead, and
the mediator calls it the way it already calls the hooks.

## Consequences

It takes two releases. Trax.Effect ships the method; until the mediator a host runs calls it,
`TrainInput` still returns `default` in the hooks, so documentation says `metadata.GetInput<T>()`
works on every version.

The method refuses a train that already has `Metadata`, because that instance has run or is
running and reads its own input, and refuses metadata whose input is not the train's input type.
A consumer calling it gains nothing a hook's `metadata` argument does not already give them, and
cannot change what a run records.

## Exemplars

**Enforced elsewhere:** `QueueHookInputTests` in Trax.Effect (the hooks read the input, the scope
restores and nests, concurrent enqueues on one instance each see their own, another instance sees
nothing, and both refusals),
`Run_WhileAnEnqueueHasHandedTheInstanceItsInput_ReadsItsOwnInput` in Trax.Effect's
`TrainLifecycleOverrideTests` (a run on the instance an enqueue handed its input to records its
own), and `QueueHookTrainInputTests` in Trax.Mediator (a key built from `TrainInput` differs per
input, and `OnQueue` reads it at the top level, on a deferring train, and in a train enqueued
from another train's hook, whose outer hook still reads its own input afterwards).

## Changelog

- **2026-09-27**: Recorded.
- **2026-09-27**: The mediator half landed; its guard is named under Exemplars.
