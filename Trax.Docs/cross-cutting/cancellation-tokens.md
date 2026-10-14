---
title: How cancellation works
description: "How a CancellationToken reaches every junction of a train, when a cancelled run stops, and why a ServiceTrain records it as Cancelled rather than Failed."
type: explanation
area: trains
nav_order: 8
source:
  - Trax.Core/src/Trax.Core/Junction/Junction.cs
  - Trax.Core/src/Trax.Core/Train/Train.cs
  - Trax.Effect/src/Trax.Effect/Services/ServiceTrain/ServiceTrain.cs
  - Trax.Effect/src/Trax.Effect.JunctionProvider.Progress/Services/CancellationCheckProvider/CancellationCheckProvider.cs
---

# How cancellation works

A train takes one `CancellationToken`, from the caller of `Run`, and every junction in the chain
sees it. A run can be stopped by an aborted HTTP request, by the host shutting down, or by an
operator pressing cancel, and in each case it stops at the same places and is recorded the same
way. The steps for writing a junction that honours the token are in
[Make a junction cancellable](/docs/cross-cutting/make-a-junction-cancellable).

## The token is a property, not a parameter

`Junction.Run(TIn input)` has no token parameter. The token lives on the train and on each junction
as a `CancellationToken` property:

```
Run(input, cancellationToken)
        │
        ▼
┌──────────────────────────┐
│  Train                   │
│  CancellationToken = ct  │
└──────────┬───────────────┘
           │  Chain<TJunction>()
           ▼
┌──────────────────────────┐
│  Junction                │
│  CancellationToken = ct  │  ← set before Run is called
│  Run(input)              │  ← reads this.CancellationToken
└──────────────────────────┘
```

`Run(input, cancellationToken)` stores the token on the train. Before each junction runs, the
chain copies it onto the junction, checks it, and only then calls `Run`. Inside a `Parallel` branch
the junction gets the branch's token instead, which is cancelled when the run is and also when a
sibling branch fails.

Keeping the token off the signature keeps a junction's contract to its input and output types, the
two things the chain matches on, and a junction that has nothing to cancel carries no unused
parameter. The cost is that the token is easy to forget: nothing makes a junction pass
`this.CancellationToken` on to the calls it makes.

`Run` without a token runs with `CancellationToken.None`. `RunEither(input)` takes no token, and a
train's `CancellationToken` cannot be set from outside the train, so a train that a host runs (the
train bus, the scheduler, a `ServiceTrain` resolved from the container) gets its token from the
host.

## Where a cancelled run stops

The chain checks the token before calling each junction's `Run`. A token that is already cancelled
when the run starts runs no junction at all, and a token cancelled between two junctions runs none
after the first.

A token cancelled while a junction is running stops it only if the junction is waiting on something
that takes the token, such as an HTTP call, a query or a `Task.Delay`. The awaited call throws
`OperationCanceledException`, and the chain treats it as cancellation. A junction that passes the
token to nothing runs to its end, and the run stops before the next junction.

## Cancellation is not a failure

A junction that throws is a failure: the exception is enriched with `TrainExceptionData` (the
junction, the train, the original stack trace) under `Exception.Data` and returned on the left
track, with its type and message unchanged.

An `OperationCanceledException` thrown while the train's token is cancelled is not enriched. It is
not the junction's fault; it is the run being told to stop. An `OperationCanceledException` thrown
while the token is not cancelled, most often an `HttpClient` timeout, is a failure like any other,
because nothing asked the run to stop: a dependency was slow.

`Run` rethrows a cancellation, so its caller sees it as an exception, which is the .NET convention.
`RunEither` lets nothing escape, so it returns the `OperationCanceledException` as `Left`, and a
caller of `RunEither` checks for it before treating a `Left` as a business failure.

## How a ServiceTrain records a cancelled run

A `ServiceTrain` records every run, and it records a cancelled one as `Cancelled`:

```
OperationCanceledException, requested     → TrainState.Cancelled
OperationCanceledException, not requested → TrainState.Failed, FailureClass Transient
Any other exception                       → TrainState.Failed
No exception                              → TrainState.Completed
```

A cancellation is requested when the run's own token was cancelled, or when its persisted cancel
flag was set: the dashboard's cancel button, `ITraxScheduler.CancelAsync`, or a scheduler timeout
for a run on another host. `CancellationRequestedException`, the `OperationCanceledException` the
flag check throws, carries that request even though the run's token was not cancelled.

A requested cancellation calls `OnCancelled`, is not retried and does not create a dead letter. It
is a deliberate act, not a fault to recover from. An unrequested one is the opposite case: it calls
`OnFailed`, is classified `Transient` unless an `IFailureClassifier` says otherwise, and a
scheduled run retries it like any other failure. The dashboard shows a cancelled run with an orange
badge.

## ServiceTrain token propagation

A `ServiceTrain` hands the caller's token to the work it does on the caller's behalf: the junctions,
the junction effects around each `EffectJunction`, and `OnStarted`. It keeps the token away from the
writes that record the run. The write that records its start, the one that records its outcome, and
the junction progress write all run on `CancellationToken.None`.

That is what makes a record of a cancelled run possible at all. The caller's token is cancelled in
exactly the case the outcome is written for, so a write that took it would never land: the row
would stay `InProgress` with no `EndTime` until the scheduler's stale-run reaper rewrote it to
`Failed`, the wrong terminal state, and a false one for a run whose work had completed. The
registered lifecycle hooks (`AddLifecycleHook`) get `CancellationToken.None` for the same reason, so
a hook that publishes the outcome is not cancelled by the cancellation it reports. A train's own
`OnCancelled`, `OnFailed` and `OnCompleted` overrides get the caller's token.

On a cancelled run, `ServiceTrain.Run` keeps the `OperationCanceledException` as the run's result,
sets `Cancelled` and `EndTime`, clears `CurrentlyRunningJunction` and `JunctionStartedAt`, saves,
then runs the `OnCancelled` hooks and rethrows. If the save itself throws, that is logged and the
`OperationCanceledException` still reaches the caller.

The same rule holds on the success path. A run whose downstream call takes no token finishes its
work after the caller has gone away, and it is recorded as `Completed`: what is recorded is what
happened, not what the caller was still waiting for. A `ServiceTrain` begins no transaction of its
own; a transaction in your junctions, and the token you pass to it, are yours.

## Stopping a run from outside

The caller's token only reaches a run started in the same process. To stop a run from the
dashboard, or one running on another host, there are two paths, and a cancel uses both:

| Path | How it reaches the run | What it stops |
|---|---|---|
| Same host | The scheduler's worker registers each run's `CancellationTokenSource` in `ICancellationRegistry`; a cancel calls `TryCancel(metadataId)` | A run on that host, at once: its token is cancelled, so the awaited call throws |
| Any host | The cancel sets `cancel_requested` on the run's metadata row; `CancellationCheckProvider` reads it before each `EffectJunction` | The run, before its next `EffectJunction` |

The flag is read by a junction effect, and junction effects run only around an `EffectJunction`. A
train built from plain `Junction`s never reads it, so a cancel or a timeout that reaches it only
through the database lets it run to the end. `AddJunctionProgress()` registers the check. The two
base classes are compared in
[EffectJunction vs Junction](/docs/core/trains-and-junctions#effectjunction-vs-junction), and what
the scheduler adds (timeouts, `CancelAsync`, shutdown) is in
[Cancellation and timeouts](/docs/scheduler/cancellation-and-timeouts).

## Background services and shutdown

The scheduler's background services pass their `stoppingToken` to the trains they run, so stopping
the host cancels the runs in flight. The worker that runs jobs gives each run a grace period first.
Both are described in [Cancellation and timeouts](/docs/scheduler/cancellation-and-timeouts).

## Testing with cancellation tokens

A test runs the train with a token it controls and checks which junctions ran. The tests are in
[Make a junction cancellable](/docs/cross-cutting/make-a-junction-cancellable#5-test-it).

## SDK Reference

> [Run / RunEither](/docs/sdk-reference/train-methods/run) | [RunAsync](/docs/sdk-reference/mediator-api/i-train-bus) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [CancelAsync](/docs/sdk-reference/scheduler-api/manifest-management)
