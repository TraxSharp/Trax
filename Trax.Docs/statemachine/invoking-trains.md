---
layout: default
title: Invoking a train
description: "A state that runs a train with the experimental Invokes: where each outcome goes, retry by re-entry, idempotent junctions, and pointers not data."
parent: State Machines
nav_order: 4
---

# Invoking a train

A state can run a train. Entering the state queues one run; the run's outcome comes back as a trigger that only
that entry of the state can apply. The state is a durable checkpoint between stages of long-running work (fetch,
normalise, resolve, embed), so a stage that fails is retried by entering its state again, not by rerunning
everything before it.

`Invokes` is experimental. Using it reports the diagnostic `TRAXEXP002` as an error; opt in by suppressing it in
the project that declares the machine:

```xml
<!-- TRAXEXP002: this project declares states that invoke a train, the experimental Invokes. -->
<NoWarn>$(NoWarn);TRAXEXP002</NoWarn>
```

## Declare it

Name the train by its interface, which is its canonical name, with its input and output types. The lambda builds
the run's input from the context the state was entered with.

```csharp
m.In(Stage.Fetching)
    .Context<SourceContext>()
    .Invokes<IFetchTrain, FetchInput, FetchOutput>(ctx => new FetchInput(ctx["source"]!.GetValue<string>()))
    .OnDone(Stage.NeedsReview,
        when: Input((FetchOutput o) => o.Unsure).IsTrue(),
        reduce: Set((FetchedContext c) => c.Fingerprint).FromInput((FetchOutput o) => o.Fingerprint))
    .OnDone(Stage.Fetched,
        reduce: Set((FetchedContext c) => c.Fingerprint).FromInput((FetchOutput o) => o.Fingerprint))
    .OnFailed(Stage.FetchFailed)
    .OnCancelled(Stage.Cancelled)
    .On(Stage.Abandon).To(Stage.Idle);
```

Every invoking state says where each outcome goes, and `Build` refuses one that does not, naming the state:

- **`OnDone`**, one or more. They are tried in the order declared, and the first whose guard holds for the train's
  output is taken, so an unguarded `OnDone` after the guarded ones is the fallback. The guard reads the output as
  the outcome's input (`Input((FetchOutput o) => ...)`), and the reduction copies what the next state needs into
  the context. An output no `OnDone` accepts moves nothing: the outcome is a `no-transition`.
- **`OnFailed`**, exactly once. A run the scheduler reaps arrives here too.
- **`OnCancelled`**, exactly once, and required: a timeout or an operator's cancel always has a declared edge.

There is no "unsure" outcome. A train that can be unsure says so in its output, and a guarded `OnDone` routes it,
as `NeedsReview` does above.

A state invokes at most one train. The input mapping runs on the server only: the IR does not carry it, and the
TypeScript twin never starts a train.

## Outcomes are their own triggers

Each outcome is a trigger of its own kind, named after the invoking state: `Fetching.done`, `Fetching.failed`
and `Fetching.cancelled`. A trigger enum member cannot contain a dot, so these never collide with yours. The
IR exports them under `outcomes`, and the success outcome's input schema is the train's output type (see the
[IR format](/docs/sdk-reference/statemachine-api/ir-format#outcomes)). The TypeScript twin applies them as
events, with the same guards and reductions as the server.

The server never takes one from a caller. `Advance` refuses an outcome trigger, and only the entry of the state
that queued the run can receive its outcome: a completion that arrives after the machine left the state is a
`no-transition`.

## What joins the reserved states

Invoking states and every `OnDone`, `OnFailed` and `OnCancelled` target join the machine's reserved states,
alongside committed states and effect targets. An autosave cannot move a draft into or out of an invoking state,
or into an outcome target. `Build` refuses an ordinary transition into an outcome target, because that state
means "the train produced this"; a self-loop on the target does not enter it and is allowed, and so is an edge
into a target that itself invokes a train, which only queues a new run (that is how a chained stage is retried). An outcome may not
go to the target of the machine's `RunsOnce` effect.

An invoked train does not count against the machine's one `RunsOnce` effect.

## Retry by entering the state again

The scheduler never retries an invoked run, and an operator cannot requeue one. A failure goes to `OnFailed`, and
the machine retries by entering the invoking state again (a `Retry` transition from the failure state, say),
which queues a new run. The old run's late completion is then a `no-transition`. The state is the checkpoint.

## Junctions must be idempotent

Queueing a run is exactly once; running it is at least once. A run can execute more than once, so every
junction in an invoked train must be safe to repeat. An irreversible step inside the train takes its own claim.
Nothing can check this for you.

## A snapshot holds pointers, not data

The outcome is reduced into the context, which is stored with the snapshot and returned to the client. Reduce
pointers into it, such as fingerprints and dataset URIs, never rows. The database writes then grow with the number
of steps, not with the amount of data each step handles.

## SDK Reference

> [Invokes](/docs/sdk-reference/statemachine-api/fluent-authoring#istatebuilder) | [OnDone / OnFailed / OnCancelled](/docs/sdk-reference/statemachine-api/fluent-authoring#iinvokebuilder) | [OutcomeSample](/docs/sdk-reference/statemachine-api/fluent-authoring#idifferentialbuilder) | [IR outcomes](/docs/sdk-reference/statemachine-api/ir-format#outcomes)
