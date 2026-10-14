---
title: "operations: execution mutations"
description: "Reference for the operations mutations on runs: cancelExecution, cancelExecutions, requeueExecution and resumeExecution."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 3
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/OperationsMutations.cs
---

# operations: execution mutations

Fields of `OperationsMutations` that act on train executions.

## cancelExecution

Requests cancellation of a single execution by metadata id. Sets the durable
`cancel_requested` flag on the row when it is still `PENDING` or `IN_PROGRESS`; the process
running the train observes it at its next junction boundary and records the run as `CANCELLED`,
and a run on the host that received the mutation is cancelled at once. It goes through the same
`IOperationsService.CancelExecutionsAsync` call as the dashboard's Cancel button.

```graphql
mutation {
  operations {
    cancelExecution(id: 100) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The execution's metadata id |

**Returns**: `OperationResponse`. `count` is `1` when the execution was flagged. An execution that is missing or already terminal returns `success: false` with `count` `0`.

A run a step of a user's state-machine draft started is refused, whatever its state, with
`success: false` and the message the dashboard shows too: a user's draft is read-only to operators,
so only its user cancels the run, by leaving the state through one of the machine's own
transitions (see [Invoking a train](/docs/statemachine/invoked-runs#operator-actions)). A run a
system-owned instance started is cancelled as any other, and the instance moves through
`OnCancelled`.

## cancelExecutions

Requests cancellation of many executions at once, as [`cancelExecution`](#cancelexecution) does for one. Every id still `PENDING` or `IN_PROGRESS` is flagged in one statement; terminal and unknown ids are skipped. Backs the dashboard's bulk cancel.

```graphql
mutation {
  operations {
    cancelExecutions(ids: [100, 101, 102]) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 execution metadata ids |

**Returns**: `OperationResponse`. `count` is the number flagged, zero included. An empty list, or more than 1000 ids, returns `success: false` and flags nothing. A run a step of a user's draft started is skipped, as [`cancelExecution`](#cancelexecution) refuses it, and the message ends with how many were skipped and why.

## requeueExecution

Re-queues an execution: reads its train name + input from the metadata row and enqueues a
fresh work queue entry for the dispatcher (the GraphQL counterpart of the dashboard's Re-queue
button). Both call [`IOperationsService.RequeueExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#requeueexecutionasync),
so they refuse the same runs with the same messages. The enqueue goes through the same path as
[`queueTrain`](/docs/sdk-reference/graphql-api/mutations/work-queue#queuetrain), so a caller who may not
run the train gets a GraphQL error with code `TRAX_AUTHORIZATION` (`"Not authorized."`) rather
than `success: false`.

The new run replays the decisions the execution recorded with
[`AddDecisionRecording`](/docs/sdk-reference/configuration/add-decision-recording), so it takes the
[tracks](/docs/core/decisions) the execution took instead of asking its deciders again. Among the API's mutations the replay link is set only here, to
the execution being re-queued, and only when it has decisions to replay (it recorded a decision,
or was itself a replaying requeue, so re-queueing a re-queue replays too); any other execution is
re-queued as an ordinary enqueue. `queueTrain` has no way to set it. A manifest's retry and a
dead-letter requeue are linked by the scheduler from its own checks, never from a caller (see
[Retries replay decisions](/docs/scheduler/how-retries-work#retries-replay-decisions)).
Each replayed answer is still checked: a question whose state hashes differently now, or whose
answer is older than `ReplayAnswersFor`, is asked afresh. See
[Re-queued and retried runs replay their decisions](/docs/effect/decisions#re-queued-and-retried-runs-replay-their-decisions).

`askAfresh: true` queues the run with no link, so it asks every question again. A run's answers are
replayed once: when a queued entry (a manifest's retry, an earlier requeue) or another run already
replays the execution, or queues a replay of it in the same instant, the new run is queued afresh
either way, and the message ends
`"It asks its deciders afresh: the decisions of execution 100 are already replayed by another run or queued entry, and are replayed once."`

An execution with no saved input is refused with `success: false` and a message saying inputs are
saved only when [`SaveTrainParameters()`](/docs/sdk-reference/configuration/save-train-parameters)
is on. So is one whose input was stored as a placeholder rather than as the input: too large to
save in full (`{"_truncated": true, ...}`), failed to serialize (`{"_unserializable": true, ...}`), or
holding a disposed `JsonDocument` (`{"_disposed": true, ...}`). Each would read as an input with every
member at its default. So is one whose recorded input has a
[`[TraxSensitive]`](/docs/sdk-reference/configuration/save-train-parameters#masking-sensitive-fields) member masked as
`{"_redacted": true}`: re-queueing it would run the train with the mask in place of the value.
An execution whose train is no longer registered is refused with
`"Train {name} is no longer registered, so execution {id} cannot be re-queued."`, and one whose
saved input no longer reads as the train's input type, because the type changed shape since, with
`"The saved input of run {id} no longer reads as {InputType.FullName}: "` and the parser's message,
never as an invalid `InputJson` the caller did not send.
Enqueue refusals (a throwing `OnQueue`, an unusable
subject key, a deferred entry cancelled before confirmation) come back as `success: false`, with
the message rule [`queueTrain`](/docs/sdk-reference/graphql-api/mutations/work-queue#queuetrain) describes, and an infrastructure failure is a masked GraphQL error, as for `queueTrain`. An enqueue reads a missing input as `{}`, so re-queueing it would re-run the train with
defaults rather than with what it ran with. This check runs before authorization, so it answers
the same for every caller; a missing execution id also returns `success: false`.

```graphql
mutation {
  operations {
    requeueExecution(id: 100) { success message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The execution's metadata id |
| `askAfresh` | `Boolean!` | No | Default `false`. When `true`, the new run asks its deciders afresh instead of replaying the execution's decisions |

**Returns**: `OperationResponse`. On success, `id` is the new **work queue entry**'s id, not the new
run's (the message reads `"Work queue entry 7 created."`). The run's metadata id appears on the entry
once the dispatcher has dispatched it:

```graphql
query {
  operations { workQueue { workQueue(id: 7) { status metadataId } } }
}
```

## resumeExecution

Resumes a failed or cancelled execution from a
[checkpoint](/docs/sdk-reference/train-methods/checkpoint) instead of running every step again: it
queues a run of the same train, with the input the execution recorded, that skips every step
before the resume point and starts from the state the checkpoint stored. With `from`, the run
resumes at that step, named by its node id as [`runGraph`](/docs/sdk-reference/graphql-api/queries/executions#rungraph)
gives it; without it, after the execution's latest checkpoint. It is the GraphQL counterpart of the
dashboard's **Resume** and **Resume from here** buttons. All three call
[`IOperationsService.ResumeExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#resumeexecutionasync),
so they refuse the same runs with the same messages.

A resume is a requeue in every check but where the run starts. It enqueues through the same path
as [`requeueExecution`](#requeueexecution), so a caller past the operations gate who may not run the
train gets a GraphQL error with code `TRAX_AUTHORIZATION` (`"Not authorized."`), and the new run
replays the execution's decisions as a requeue's would. It needs no role a requeue does not.

It is refused with `success: false`, the reason as the message, and nothing queued when:

- no execution has the id, or it is not `FAILED` or `CANCELLED` (`"Execution 100 is Completed; only a failed or cancelled run can be resumed."`);
- a state machine's step started it, since only that step receives its outcome;
- its saved input is missing, a placeholder or masked, for the reasons `requeueExecution` gives;
- a resume of it is already queued (`"A resume of execution 100 is already queued (WorkQueue 7); a run is resumed once at a time. Nothing was queued."`);
- a resume of it already completed, so its work is done (`"Execution 100 cannot be resumed: a resume of run 100 already completed, so its work is done. Requeue it to run it again from the top. Nothing was queued."`);
- its train is no longer registered here, or its chain cannot be read;
- no checkpoint it wrote lets it resume at that step: none comes before it, a step from the point
  on needs a value nothing restores, or the stored checkpoint no longer matches the running chain
  or the state's shape. The resume check's own reason is the message, for example
  `"No checkpoint the run wrote comes before 'FetchSources#0', so it can only run again from the top."`

`runGraph` says beforehand where a run can resume: `canResume` on the graph for a resume after the
latest checkpoint, and on each node for a resume there.

```graphql
mutation {
  operations {
    resumeExecution(id: 100, from: "SummarizeSources#0") { success message id }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The execution's metadata id |
| `from` | `String` | No | The node id of the step to resume at. Omitted, the run resumes after its latest checkpoint |

**Returns**: `OperationResponse`. On success, `id` is the new **work queue entry**'s id, as for
`requeueExecution`. The entry names the execution it resumes and the step (`resume_from` and
`resume_at` on the work queue row), and the run copies both when it is dispatched.

The run checks the resume again as it starts. If it can no longer be honoured (a deploy changed
the chain or the state between the queueing and the dispatch, say), the run fails, classified
permanent, with `ResumeRefusedException` and the reason as its failure, rather than running from
the top: a resume was asked for, not a fresh run.

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions)
- [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service)
