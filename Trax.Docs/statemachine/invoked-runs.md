---
title: "Invoked runs: outcomes and refusals"
description: "Reference for trains a state machine state invokes: outcome triggers, reserved states, delivery, outcomes, OnFailed reasons, host refusals and operator actions."
type: reference
area: machines
nav_order: 23
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Invokes.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokeOutcomeDelivery.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokeOutcomeReconciler.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokedRunOutcomes.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokesStartupValidator.cs
  - Trax.Mediator/src/Trax.Mediator/Services/InvokedTrains/InvokedTrainLauncher.cs
---

# Invoked runs: outcomes and refusals

A state declared with `Invokes<TTrain, TInput, TOutput>` queues one run of the train each time it is entered, and
applies the run's outcome to that entry of the state. Declaring one is covered in
[Invoke a train from a state](/docs/statemachine/invoking-trains); the builder methods are under
[IInvokeBuilder](/docs/sdk-reference/statemachine-api/machine#iinvokebuilder).

## Outcome triggers

Each outcome is a trigger of its own kind, named after the invoking state:

| Trigger | Input |
| --- | --- |
| `<State>.done` | the train's output, as the outcome's input |
| `<State>.failed` | none |
| `<State>.cancelled` | none |

A trigger enum member cannot contain a dot, so these never collide with a declared trigger. The IR exports them under
[`outcomes`](/docs/sdk-reference/statemachine-api/ir-format#outcomes), and the TypeScript twin applies them as events
with the same guards and reductions as the server. The input mapping of `Invokes` runs on the server only: the IR
does not carry it, and the twin never starts a train. The server never takes an outcome trigger from a caller:
`Advance` refuses one as `OUTCOME_BOUND`, and a completion that arrives after the machine left the state is a
`NO_TRANSITION`.

## Reserved states

Invoking states and every `OnDone`, `OnFailed` and `OnCancelled` target join the machine's reserved states, beside
committed states and effect targets.

| Rule | Detail |
| --- | --- |
| Autosave | cannot move a draft into or out of an invoking state, or into an outcome target. The generic save refuses every save of a machine that invokes a train as `SAVE_NOT_ALLOWED`. |
| Ordinary transition into an outcome target | refused by `Build`. A self-loop on the target, and an edge into a target that itself invokes a train, are allowed. |
| Outcome into the `RunsOnce` effect's target, or into the start state | refused by `Build`. |
| The `RunsOnce` count | an invoked train does not count against the machine's one effect. |
| Trains per state | at most one. |

## Entering and leaving the state

| Event | Written |
| --- | --- |
| Entering the state, by an advance, a send, or `IMachineInstances.Start` when the initial state invokes | in one transaction, on a data context of its own: the snapshot, the run's work queue entry, and the row's server-only invoke token, which is the entry's external id. A crash or a refusal leaves none of them. |
| Leaving through any declared transition | the token is cleared in the same write and the run cancelled: a queued run is marked cancelled, and a dispatched one has its cancel flag set, which it reads at its next junction on whichever host runs it. |
| A self-loop on the invoking state | nothing: the state keeps its run. |

The enqueue goes through the mediator: the train is found by its canonical name, authorized, its input capped and
its subject key stamped. On Postgres the entry's insert wakes the dispatchers on every host when the transaction
commits; on SQLite a dispatcher in the same process is woken.

The entry and the run record which machine, instance and owner kind queued them:

| Column (`trax.work_queue`, `trax.metadata`) | Property (`WorkQueue`, `Metadata`) |
| --- | --- |
| `invoking_machine` | `InvokingMachine` |
| `invoking_instance_id` | `InvokingInstanceId` |
| `invoking_owner_kind` | `InvokingOwnerKind` |

All three are null on a run no machine invoked. The launcher writes them through `CreateWorkQueue.InvokedBy`, an
`InvokedBy(Machine, InstanceId, OwnerKind)`, and the dispatcher copies them to the run through
`CreateMetadata.InvokedBy`. A run carrying the link cannot be requeued, and its end is delivered to that instance.

## Outcomes

| The run | Outcome |
| --- | --- |
| completed | `OnDone`, with the output the run recorded for its machine |
| failed by its train, or by the scheduler (reaped as stale, failed at dispatch or on startup recovery) | `OnFailed` |
| cancelled: a timeout, or, for a system-owned instance, an operator's cancel of the instance, of its run, or of its entry before it was dispatched | `OnCancelled` |
| cancelled because its state was left | none: the token was cleared when the state was left |
| still queued, requeued after a failed dispatch, or running | none yet |

`OnDone` edges are tried in declaration order, and the first whose guard holds for the output is taken. The run is
queued exactly once, runs at least once, and its outcome is applied exactly once.

## Delivery

An outcome is applied to the one row whose invoke token is the run's external id, by a single conditional update
(`UPDATE ... WHERE invoke_token = @token`) that also replaces or clears the token. Every other delivery of the same
run, from any host, matches nothing and is a `NO_TRANSITION` that writes nothing; so is a run whose state was left or
entered again under a new token.

| Path | Runs on | Covers |
| --- | --- | --- |
| Lifecycle hook | the host that ran the train, when it registers the machine | the run, as soon as its terminal write commits |
| Outcome reconciler (a hosted service) | every host that registers machines | a host that died after the terminal write, a run the scheduler failed or cancelled, a cancel before dispatch, a train run on a host that registers no machines |

| Reconciler setting | Value |
| --- | --- |
| Sweep interval | `StateMachineOptions.InvokeOutcomeSweepInterval`, 5 seconds |
| Sweep interval while a Postgres host hears run-end notifications | `InvokeOutcomeSweepIntervalWhileListening`, 1 minute; a full sweep each time it subscribes again |
| Postgres | a trigger notifies every host when an invoked run ends, and each reconciler delivers that run at once; notifications are batched and deduplicated |
| SQLite | the sweep runs at its interval |
| What a sweep reads | one query per page, only instances of the machines its host registers whose run has ended |
| An instance whose snapshot the host cannot read (written under a newer machine version) | left to a host that can; retried after a wait that doubles from 30 seconds to an hour |

A completed run writes the output its machine reads in its own terminal write, in the same statement that records it
completed, serialized with the names the machine's guards and reductions use. It is not the run's recorded `output`,
which a host may redact, bound or not keep. The copy is never shown on an operator surface.

When an outcome's target invokes a train, applying the outcome queues that run in the same transaction, under the new
token. Only a system-owned machine may declare such an edge.

## OnFailed reasons

An outcome is applied as the state's `OnFailed`, with a reason, when its own outcome cannot be applied or the run is
gone:

| Reason | When |
| --- | --- |
| `invoke-outcome-too-large` | the output is past 64 KiB, so it was never stored; or the snapshot its reduction produces is past the 64 KiB snapshot cap |
| `invoke-output-unaccepted` | no `OnDone` guard accepts the output |
| `invoke-output-unrecorded` | the output could not be serialized for the machine |
| `invoke-outcome-rejected` | the chosen edge's reduction threw, or produced a context its target refuses |
| `invoke-next-run-refused` | the target invokes a train of its own, and that run could not be queued |
| `invoke-run-missing` | neither the run's work queue entry nor its execution record exists, so it can never end |

Each is logged at warning level with the run, the machine, the instance and both states, never with the output or
the context. When `OnFailed` (or `OnCancelled`) cannot be applied either, the token is cleared and the instance stays
in the invoking state with no live run, logged at error level with its reason; it leaves through one of its declared
transitions. The same write records the state in `invoke_stranded_state` (`SnapshotDraft.InvokeStrandedState`), so
`TraxInvariants` reports only an instance that lost its token by mistake; the mark is cleared when the instance gets a
run or moves to another state.

A run's records are kept while its outcome is to come: the work queue entry is written in the same transaction that
gives the instance its token, nothing deletes a queued entry, and metadata retention keeps an invoked run and its
entry while an instance still holds its token.

## Ownership and limits

| | User-owned (the default) | System-owned (`SystemOwned()`) |
| --- | --- | --- |
| Instances created by | the user's `start` | `IMachineInstances.Start` only |
| Moved by | the user's events and the runs' outcomes | the runs' outcomes and `IMachineInstances.Advance` |
| Train authorized | against the user entering the state, at entry; refused as `INVOKE_FORBIDDEN` | in the trusted execution scope, as a scheduled manifest run |
| Outcome into another invoking state | refused at startup | allowed |
| Live invoked runs per user | 10 across every machine, or the machine's `InvokedRunLimit(n)`; past it `INVOKE_LIMIT_REACHED` | not capped here; the dispatcher's `MaxActiveJobs` bounds them |

A run is live from the entry that queued it until it ends. Leaving its state cancels a queued run at once; a
dispatched run counts until it stops at its next junction. A machine's `InvokedRunLimit(n)` is compared with the same
count across all machines, so the most one user can hold is the largest limit among the host's machines.

The scheduler never retries or dead-letters an invoked run (it has no manifest). `requeueExecution` and the
dashboard's Re-queue refuse one with the same reason.

## Host refusals

The host refuses to start when a machine invokes a train it cannot queue in the advance's transaction, cancel from
another host, or authorize as declared. Each refusal names the machine, the state and the train, junction or member
at fault.

| Refused | Why |
| --- | --- |
| a train that is not registered with `AddMediator(...)` | the run cannot be queued or dispatched |
| a plain `Junction` anywhere in the train's chain, inside a `Parallel` branch or a routing step's tracks too | only an `EffectJunction` reads the run's cancel flag, so leaving the state could not stop it on another host |
| an `IChain<I>` whose registered class is not an `EffectJunction`, or that the container builds with a factory | the same; the junction it runs must be known |
| a train that is not a `ServiceTrain` | only a `ServiceTrain`'s run is sealed to its junctions |
| anything the chain recorder refuses, or a chain that cannot be read outside a request | the chain cannot be checked |
| no `AddJunctionProgress()` | it registers the junction effect that reads the cancel flag |
| no `AddMediator(...)` | nothing can queue the runs |
| an `ITrainExecutionService` registered in place of the mediator's | the runs are queued through the mediator's own, inside the transaction that enters the state |
| an `OnQueue` override or `DeferQueuePromotion` on the train | both commit on their own, outside the transaction that enters the state |
| the InMemory provider | it has no transactions |
| on a user-owned machine, a train whose `[TraxAuthorize]` names roles or a policy that an event entering the state (or the start into an invoking initial state) does not also require | entering the state runs the train as whoever sent the event |
| on a user-owned machine, a `[TraxBroadcast]` train | its subscribers see every run's output |
| on a system-owned machine, a train that declares `[TraxAuthorize]` | the trusted scope does not check user requirements |
| on a user-owned machine, an `OnDone`, `OnFailed` or `OnCancelled` that enters a state invoking a train | the next run would be queued with no user present to authorize it |
| on a user-owned machine, a timer that enters a state invoking a train | the same |
| an output type that reaches a `[TraxSensitive]` member | the output is reduced into a context stored as plain JSON and returned to the client |

The `[TraxBroadcast]` and `[TraxSensitive]` refusals are made again at runtime: entering the state throws and queues
nothing, and a sensitive output that reaches a run anyway is neither recorded for the machine nor applied to the
context; the state goes to its `OnFailed` instead.

`EffectJunction`'s railway step is sealed, so a subclass cannot skip the cancel check. A slow decider in `Decide` or
`Gate` runs outside any junction, so no cancel check happens while it runs, and a train started from inside a
junction is not cancelled with its parent.

## Operator actions

| Surface | Behaviour |
| --- | --- |
| The dashboard's State Machines pages, the GraphQL [`machineInstances` and `machineInstance`](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstances) queries | every instance, system-owned and users' drafts alike: its state, timestamps, owner kind, and the runs it invoked, newest first, with the one its state waits on marked live. Never its context, and never a run's input or output. |
| Runs listed for a system instance | every run it invoked (a system instance is unique by machine and id) |
| Runs listed for a user's draft | only its live run, the one its own token names |
| The dashboard's Cancel on an instance, [`cancelMachineInstance`](/docs/sdk-reference/graphql-api/mutations/machine-instances#cancelmachineinstance) | one service method, the same outcome and message. Cancels a system-owned instance's live run, and the instance then moves through `OnCancelled`. Refused for a user's draft, an instance whose state waits on no run, and one whose run has already ended. |
| `cancelExecution`, `cancelWorkQueueEntry`, the dashboard's Cancel on a run's or an entry's page | refused, with one message on both surfaces, for a run or a queued entry a step of a user's draft started; their bulk forms skip such rows and say how many. A system-owned instance's run can be cancelled, and the instance moves through `OnCancelled`. |

A run records the machine, the instance id and the owner kind that queued it, not the user. No Trax-provided
operation creates or advances a system instance; a host can advance one from its own code through
[`IMachineInstances.Advance`](/docs/sdk-reference/statemachine-api/machine-instances) under its own authorization, as
the Recovery sample's `partitionAction` does. The dashboard pages are described in [Dashboard](/docs/dashboard).

## See also

- [Invoke a train from a state](/docs/statemachine/invoking-trains)
- [Persistence ports: invoked-run ports](/docs/sdk-reference/statemachine-api/persistence-ports#invoked-run-ports)
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes)
