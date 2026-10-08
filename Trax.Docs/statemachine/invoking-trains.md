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

An autosave of a draft in an invoking state is refused as `draft-invoking` unless it is identical to what is
stored: a save could neither leave the state, which would strand the run, nor rewrite the context the run was
started from. An `advanceSnapshot` with an outcome trigger is refused as `outcome-bound`.

## Entering and leaving the state

Entering an invoking state, by an advance, a send, or `IMachineInstances.Start` when the initial state invokes,
writes three things in one transaction on the request's data context: the snapshot, the run's work queue entry,
and the row's server-only invoke token, which is the entry's external id. A crash or a refusal anywhere in between
leaves none of them, so no machine waits on a run that was never queued and no run is queued for a machine that
never moved. On Postgres the entry's insert wakes the dispatchers on every host when the transaction commits; on
SQLite a dispatcher in the same process is woken.

The enqueue goes through the mediator like any caller's: the train is found by its canonical name, authorized, its
input capped and its subject key stamped. The entry and the run it becomes record which machine, instance and owner
kind queued them (`invoking_machine`, `invoking_instance_id`, `invoking_owner_kind` on `trax.work_queue` and
`trax.metadata`), so a run stays linked to its instance after the token is cleared.

Leaving the state through any declared transition (a user's own "stop" event, say) clears the token in the same
write and cancels the run: a run still queued is marked cancelled, and one already dispatched has its cancel flag
set, which it reads at its next junction on whichever host runs it. A self-loop on the invoking state neither
leaves nor enters it, and the state keeps its run.

## Who a run belongs to

A machine is user-owned unless it declares `SystemOwned()`, and that decides how its runs are authorized:

- **A user-owned machine's** train is authorized against the user entering the state, at entry, through the
  host's train authorization. A user the train refuses cannot enter the state (`invoke-forbidden`), and nothing is
  written.
- **A system-owned machine's** instances are created only by
  [`IMachineInstances.Start`](/docs/sdk-reference/statemachine-api/machine-instances), and no user's draft
  operation reaches the machine. Its train is authorized inside Trax's trusted execution scope, as a scheduled
  manifest run is.

One user holds at most 10 live invoked runs in a machine; entering an invoking state past that is refused as
`invoke-limit-reached`. A machine sets its own limit with `InvokedRunLimit(n)`. A run is live from the entry that
queued it until its state is left or its outcome is applied. System owners are not capped here: the dispatcher's
`MaxActiveJobs` bounds them.

## What the host must provide

The host refuses to start when a machine invokes a train it cannot queue in the advance's transaction, cancel from
another host, or authorize as declared. Each refusal names the machine, the state and the train, junction or member
at fault:

| Refused | Why |
| --- | --- |
| a plain `Junction` anywhere in the train's chain, inside a `Parallel` branch or a routing step's tracks too | only an `EffectJunction` reads the run's cancel flag, so leaving the state could not stop it on another host |
| an `IChain<I>` whose registered class is not an `EffectJunction`, or that the container builds with a factory | the same; the junction it runs must be known |
| a train that is not a `ServiceTrain` | only a `ServiceTrain`'s run is sealed to its junctions |
| anything the chain recorder refuses, or a chain that cannot be read outside a request | the chain cannot be checked |
| no `AddJunctionProgress()` | it registers the junction effect that reads the cancel flag |
| no `AddMediator(...)` | nothing can queue the runs |
| an `OnQueue` hook or `DeferQueuePromotion` on the train | both commit on their own, outside the transaction that enters the state |
| the InMemory provider | it has no transactions |
| on a user-owned machine, a train whose `[TraxAuthorize]` names roles or a policy | entering the state would be a way around a requirement stricter than the machine's own mutations |
| on a user-owned machine, a `[TraxBroadcast]` train | its subscribers see every run's output |
| on a system-owned machine, a train that declares `[TraxAuthorize]` | the trusted scope does not check user requirements |
| an output type that reaches a `[TraxSensitive]` member | the output is reduced into a context stored as plain JSON and returned by `loadSnapshot` |

`EffectJunction`'s railway step is sealed, so a subclass cannot skip the check. Two limits remain: a slow decider in
`Decide` or `Gate` runs outside any junction, so no cancel check happens while it runs, and a train started from
inside a junction is not cancelled with its parent.

## Retry by entering the state again

The scheduler never retries an invoked run: it has no manifest, so it is never retried or dead-lettered. An
operator cannot requeue one either: `requeueExecution` and the dashboard's Re-queue refuse it with the same reason.
A failure goes to `OnFailed`, and the machine retries by entering the invoking state again (a `Retry` transition
from the failure state, say), which queues a new run under a new token. The old run's late completion is then a
`no-transition`. The state is the checkpoint.

## Junctions must be idempotent

Queueing a run is exactly once; running it is at least once. A run can execute more than once, so every
junction in an invoked train must be safe to repeat. An irreversible step inside the train takes its own claim.
Nothing can check this for you.

## A snapshot holds pointers, not data

The outcome is reduced into the context, which is stored with the snapshot and returned to the client. Reduce
pointers into it, such as fingerprints and dataset URIs, never rows. The database writes then grow with the number
of steps, not with the amount of data each step handles.

## SDK Reference

> [Invokes](/docs/sdk-reference/statemachine-api/fluent-authoring#istatebuilder) | [SystemOwned / InvokedRunLimit](/docs/sdk-reference/statemachine-api/fluent-authoring#imachinebuilder) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances) | [IInvokedTrainLauncher](/docs/sdk-reference/statemachine-api/persistence-ports#iinvokedtrainlauncher) | [Result codes](/docs/sdk-reference/statemachine-api/result-codes) | [OnDone / OnFailed / OnCancelled](/docs/sdk-reference/statemachine-api/fluent-authoring#iinvokebuilder) | [OutcomeSample](/docs/sdk-reference/statemachine-api/fluent-authoring#idifferentialbuilder) | [IR outcomes](/docs/sdk-reference/statemachine-api/ir-format#outcomes)
