---
layout: default
title: Invoking a train
description: "A state that runs a train with Invokes: where each outcome goes, retry by re-entry, idempotent junctions, and pointers not data."
parent: State Machines
nav_order: 4
---

# Invoking a train

A state can run a train. Entering the state queues one run; the run's outcome comes back as a trigger that only
that entry of the state can apply. The state is a durable checkpoint between stages of long-running work (fetch,
normalise, resolve, embed), so a stage that fails is retried by entering its state again, not by rerunning
everything before it.

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
  the context. To the engine, and to the TypeScript twin, an output no `OnDone` accepts is a `no-transition`. On
  the server the run has finished, so the instance must not wait on it: such an output is applied as the state's
  `OnFailed`, with the reason `invoke-output-unaccepted` (see [How an outcome comes back](#how-an-outcome-comes-back)).
- **`OnFailed`**, exactly once. A run the scheduler reaps arrives here too.
- **`OnCancelled`**, exactly once, and required: a timeout or an operator's cancel always has a declared edge.

The output is a record with properties. `Build` refuses a tuple output, naming the state: a tuple's elements are
fields, which the output's stored JSON does not carry, so no guard or reduction could read them.

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
into a target that itself invokes a train, which only queues a new run (that is how a chained stage is retried, and
how a user-owned machine chains its stages at all; see [Who a run belongs to](#who-a-run-belongs-to)). An outcome
may not go to the target of the machine's `RunsOnce` effect, nor to the state the machine starts at: that state would
be reserved, so no save could create a draft of the machine at all. Send the outcome to a state of its own, with an
edge from there back to the start if the flow returns to it.

An invoked train does not count against the machine's one `RunsOnce` effect.

An autosave of a draft in an invoking state is refused as `draft-invoking` unless it is identical to what is
stored: a save could neither leave the state, which would strand the run, nor rewrite the context the run was
started from. An `advanceSnapshot` with an outcome trigger is refused as `outcome-bound`.

## Entering and leaving the state

Entering an invoking state, by an advance, a send, or `IMachineInstances.Start` when the initial state invokes,
writes three things in one transaction: the snapshot, the run's work queue entry, and the row's server-only invoke
token, which is the entry's external id. A crash or a refusal anywhere in between leaves none of them, so no machine
waits on a run that was never queued and no run is queued for a machine that never moved. The transaction is on a
data context of its own, never the request's, so a junction that starts or advances an instance does not commit or
lose the writes its own run has tracked. On Postgres the entry's insert wakes the dispatchers on every host when the transaction commits; on
SQLite a dispatcher in the same process is woken.

The enqueue goes through the mediator like any caller's: the train is found by its canonical name, authorized, its
input capped and its subject key stamped. The entry and the run it becomes record which machine, instance and owner
kind queued them (`invoking_machine`, `invoking_instance_id`, `invoking_owner_kind` on `trax.work_queue` and
`trax.metadata`), so a run stays linked to its instance after the token is cleared.

Leaving the state through any declared transition (a user's own "stop" event, say) clears the token in the same
write and cancels the run: a run still queued is marked cancelled, and one already dispatched has its cancel flag
set, which it reads at its next junction on whichever host runs it. A self-loop on the invoking state neither
leaves nor enters it, and the state keeps its run.

## How an outcome comes back

When the run ends, its outcome is applied to the one row whose invoke token is the run's external id, by a single
conditional update (`UPDATE ... WHERE invoke_token = @token`) that also replaces or clears the token. Whichever
delivery matches first applies it; every other delivery of the same run, from any host, matches nothing and is a
typed `no-transition` that writes nothing. A run whose state was left, or whose state was entered again under a
new token, has nowhere to land, which is how a late completion after a cancel or a retry is ignored.

How the run ended decides the outcome:

| The run | Outcome |
| --- | --- |
| completed | `OnDone`, with the output the run recorded for its machine |
| failed by its train, or by the scheduler (a run reaped as stale, failed at dispatch or on startup recovery) | `OnFailed` |
| cancelled: a timeout, or, for a system-owned instance, an operator's cancel of the instance, of its run, or of its entry before it was dispatched | `OnCancelled` |
| cancelled because its state was left | nothing: the token was cleared when the state was left |
| still queued, requeued after a failed dispatch, or running | nothing yet |

**Two paths deliver it.** The host that ran the train delivers the outcome as soon as the run's terminal write has
committed, through a lifecycle hook, when that host registers the machine. Every host that registers machines also
runs an outcome reconciler, a hosted service that sweeps the rows holding a live token, finds those whose run has
ended, and delivers each. The hook is the fast path; the sweep is the guarantee, and covers a host that died after
the run's terminal write and before its hook, a run the scheduler failed or cancelled itself, a cancel before
dispatch, and a train run on a host that registers no machines (a dedicated scheduler host, typically). On Postgres
a trigger notifies every host when an invoked run ends, and each reconciler delivers that run at once; on SQLite
the sweep runs at its interval. Set the interval with `StateMachineOptions.InvokeOutcomeSweepInterval` (5 seconds
by default):

```csharp
trax.AddStateMachines(
    o => o.InvokeOutcomeSweepInterval = TimeSpan.FromSeconds(10),
    typeof(IngestMachine).Assembly);
```

**The output survives any crash.** A completed run writes the output its machine reads in its own terminal write,
in the same statement that records it completed, serialized with the names the machine's guards and reductions
use. It is not the run's recorded `output`, which a host may redact, bound or not keep at all. So once a run is
recorded completed, its output is there for whichever host applies the outcome, and until it is, the run has not
completed: there is no point at which a crash loses an outcome or applies one twice. The copy is never shown on
any operator surface, the execution views, the work queue views and the state machine views alike.

**When the outcome target invokes a train,** applying the outcome queues that run in the same transaction, under
the new token, exactly as entering the state from an advance does. Only a system-owned machine may declare such an
edge, and its run is authorized in the trusted execution scope like the machine's others; the startup check refuses
a user-owned machine that does, because no user is present to authorize the run.

**A finished run never leaves its instance waiting, and neither does a lost one.** An outcome is applied as the
state's `OnFailed`, with a reason, when its own outcome cannot be, or when the run is gone:

| Reason | When |
| --- | --- |
| `invoke-outcome-too-large` | the output is past 64 KiB, so it was never stored; or the snapshot its reduction produces is past the 64 KiB snapshot cap |
| `invoke-output-unaccepted` | no `OnDone` guard accepts the output |
| `invoke-output-unrecorded` | the output could not be serialized for the machine |
| `invoke-outcome-rejected` | the chosen edge's reduction threw, or produced a context its target refuses |
| `invoke-next-run-refused` | the target invokes a train of its own, and that run could not be queued |
| `invoke-run-missing` | the run can no longer be found: neither its work queue entry nor its execution record exists, so it was deleted before its outcome was delivered and can never end |

Each is logged at warning level with the run, the machine, the instance and both states, never with the output or
the context. If even `OnFailed` (or `OnCancelled`) cannot be applied, the token is cleared and the instance stays
in the invoking state with no live run, logged at error level with its reason; it leaves through one of its declared
transitions.

A run's records cannot vanish while it is still to come: its work queue entry is written in the same transaction
that gives the instance its token, and nothing deletes a queued entry. Metadata retention keeps an invoked run, and
its entry, while an instance still holds its token, so a run that outlives its retention before its outcome is
delivered is still delivered; once the outcome is applied the run is deleted like any other expired run.

## Queued once, run at least once, applied once

The run is queued exactly once, with the advance that enters the state. It runs at least once: a run can execute
more than once (see below), so its junctions must be idempotent. Its outcome is applied exactly once, by the
conditional update on the token, however many hosts deliver it and however often.

## Who a run belongs to

A machine is user-owned unless it declares `SystemOwned()`, and that decides how its runs are authorized:

- **A user-owned machine's** train is authorized against the user entering the state, at entry, through the
  host's train authorization. A user the train refuses cannot enter the state (`invoke-forbidden`), and nothing is
  written.
- **A system-owned machine's** instances are created only by
  [`IMachineInstances.Start`](/docs/sdk-reference/statemachine-api/machine-instances), moved from code only by
  its runs' outcomes and `IMachineInstances.Advance` (a `Retry`, say), and no user's draft operation reaches the
  machine. Its train is authorized inside Trax's trusted execution scope, as a scheduled
  manifest run is.

Only a system-owned machine chains runs through outcomes. A run an outcome queues has no user present to authorize
it, so a user-owned machine whose `OnDone`, `OnFailed` or `OnCancelled` enters a state that invokes a train is
refused at startup; it chains its stages through an event the user sends instead (a "continue" or "retry" edge into
the next stage), which is authorized as that user.

One user holds at most 10 live invoked runs, counted across every machine; entering an invoking state past that is
refused as `invoke-limit-reached`. A run is live from the entry that queued it until it ends. Leaving its state
cancels a queued run at once, but a run already dispatched is only flagged and stops at its next junction, so it
counts until then. A machine sets its own limit with `InvokedRunLimit(n)`, compared with the same count: its invoking
states are refused once the user holds `n` live runs in all machines together, so the most one user can hold is the
largest limit among the host's machines. System owners are not capped here: the dispatcher's `MaxActiveJobs` bounds
them.

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
| an `ITrainExecutionService` registered in place of the mediator's | the runs are queued through the mediator's own, inside the transaction that enters the state |
| an `OnQueue` hook or `DeferQueuePromotion` on the train | both commit on their own, outside the transaction that enters the state |
| the InMemory provider | it has no transactions |
| on a user-owned machine, a train whose `[TraxAuthorize]` names roles or a policy | entering the state would be a way around a requirement stricter than the machine's own mutations |
| on a user-owned machine, a `[TraxBroadcast]` train | its subscribers see every run's output |
| on a system-owned machine, a train that declares `[TraxAuthorize]` | the trusted scope does not check user requirements |
| on a user-owned machine, an `OnDone`, `OnFailed` or `OnCancelled` that enters a state invoking a train | the next run would be queued with no user present to authorize it; chain through a user event, or declare `SystemOwned()` |
| an output type that reaches a `[TraxSensitive]` member | the output is reduced into a context stored as plain JSON and returned by `loadSnapshot` |

The `[TraxBroadcast]` and `[TraxSensitive]` refusals are made again at runtime, so a host whose startup check never
ran still cannot break them: entering the state throws and queues nothing, and a sensitive output that reaches a run
anyway is neither recorded for the machine nor applied to the context; the state goes to its `OnFailed` instead.

`EffectJunction`'s railway step is sealed, so a subclass cannot skip the check. Two limits remain: a slow decider in
`Decide` or `Gate` runs outside any junction, so no cancel check happens while it runs, and a train started from
inside a junction is not cancelled with its parent.

## Retry by entering the state again

The scheduler never retries an invoked run: it has no manifest, so it is never retried or dead-lettered. An
operator cannot requeue one either: `requeueExecution` and the dashboard's Re-queue refuse it with the same reason.
A failure goes to `OnFailed`, and the machine retries by entering the invoking state again (a `Retry` transition
from the failure state, say), which queues a new run under a new token. The old run's late completion is then a
`no-transition`. The state is the checkpoint.

## Operators

Operators see instances and act on one kind of them, under the operations gate. The dashboard's
[State Machines pages](/docs/dashboard#state-machines) and the GraphQL
[`machineInstances` and `machineInstance`](/docs/sdk-reference/graphql-api/queries#machineinstances)
show every instance, system-owned and users' drafts alike: its state, timestamps, owner kind and
the runs it invoked, newest first, with the one its state waits on marked live. Never its
context, which is an untyped JSON object nothing can mask, and never a run's input or output.

A run records the machine, the instance id and the owner kind that queued it, not the user. A
system instance is unique by machine and id, so it lists every run it invoked. Several users can
each hold a draft under one id, so a user's draft lists only its live run, the one its own token
names, and never a run another user's draft under the same id may have queued.

The one action is cancel, and only on a system-owned instance: the dashboard's Cancel button
(after a confirmation) and
[`cancelMachineInstance`](/docs/sdk-reference/graphql-api/mutations#cancelmachineinstance) call one
service method and return the same outcome and message. It cancels the live run, as an operator's
cancel of the run itself does: a run still queued is marked Cancelled and never starts, and a
dispatched run has its cancel requested and stops at its next junction. The instance then moves
through its state's `OnCancelled` edge, applied once by the same conditional update every
delivery makes. A user's draft is refused, as are an instance whose state waits on no run and one
whose run has already ended.

A user's draft is read-only to operators, so its run is too: `cancelExecution` and
`cancelWorkQueueEntry`, and the dashboard's Cancel on the run's and the entry's pages, refuse a
run or a queued entry a step of a user's draft started, with one message on both surfaces, and
their bulk forms skip such rows and say how many they skipped. Only its user cancels it, by leaving
the state through one of the machine's own transitions. A system-owned instance's run can be
cancelled either way, and the instance then moves through `OnCancelled`.

No Trax-provided operation creates or advances a system instance. A host can advance one from its
own code through [`IMachineInstances.Advance`](/docs/sdk-reference/statemachine-api/machine-instances),
and expose that under its own authorization, as the Recovery sample's `partitionAction` does.

## Junctions must be idempotent

Queueing a run is exactly once; running it is at least once. A run can execute more than once, so every
junction in an invoked train must be safe to repeat. An irreversible step inside the train takes its own claim.
Nothing can check this for you.

## A snapshot holds pointers, not data

The outcome is reduced into the context, which is stored with the snapshot and returned to the client. Reduce
pointers into it, such as fingerprints and dataset URIs, never rows. The database writes then grow with the number
of steps, not with the amount of data each step handles.

## SDK Reference

> [Invokes](/docs/sdk-reference/statemachine-api/fluent-authoring#istatebuilder) | [SystemOwned / InvokedRunLimit](/docs/sdk-reference/statemachine-api/fluent-authoring#imachinebuilder) | [InvokeOutcomeSweepInterval](/docs/sdk-reference/statemachine-api/add-trax-state-machines#options) | [IMachineInstances](/docs/sdk-reference/statemachine-api/machine-instances) | [Invoked-run ports](/docs/sdk-reference/statemachine-api/persistence-ports#invoked-run-ports) | [Result codes](/docs/sdk-reference/statemachine-api/result-codes) | [OnDone / OnFailed / OnCancelled](/docs/sdk-reference/statemachine-api/fluent-authoring#iinvokebuilder) | [OutcomeSample](/docs/sdk-reference/statemachine-api/fluent-authoring#idifferentialbuilder) | [IR outcomes](/docs/sdk-reference/statemachine-api/ir-format#outcomes)
