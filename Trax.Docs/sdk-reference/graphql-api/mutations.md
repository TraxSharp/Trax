---
layout: default
title: Mutations
description: "Reference for the Trax GraphQL mutations: dispatch mutations generated for TraxMutation trains, and opt-in operations mutations for manifests and dead letters."
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 3
---

# Mutations

Mutations are organized into two groups under the root `Mutation` type:

```graphql
type Mutation {
  dispatch: DispatchMutations!     # only when [TraxMutation] trains exist
  operations: OperationsMutations! # only when ExposeOperationMutations() is set
}
```

- **`dispatch`**: auto-generated typed mutations for trains annotated with [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
- **`operations`**: scheduler management operations (trigger, disable, enable, cancel manifests and groups, plus the nested `deadLetters` namespace for requeue/acknowledge). **Off by default**, opt in with [`ExposeOperationMutations()`](/docs/sdk-reference/graphql-api/add-trax-graphql) on the builder. The Trax scheduler is reachable through these mutations, so leaving them open lets any caller disrupt scheduled work.

The root `Mutation` type is omitted entirely when no source contributes to it (no `[TraxMutation]` train and `ExposeOperationMutations()` not called).

## Dispatch Mutations (Auto-Generated)

Trax auto-generates strongly-typed mutations for trains that opt in with `[TraxMutation]`. Only trains with this attribute appear under `dispatch`. Trains annotated with `[TraxQuery]` appear under `query { discover { ... } }` instead; see [Queries](/docs/sdk-reference/graphql-api/queries).

Each whitelisted train gets a single mutation field named after the train (no prefix). Trains with `Namespace` set are grouped under a sub-namespace (e.g. `dispatch { alerts { createAlert } }`). The mutation's parameters and behavior depend on the operations passed to the attribute constructor:

- **Run + Queue (default)**: when no operations are specified (or both `GraphQLOperation.Run` and `GraphQLOperation.Queue` are passed), the mutation accepts an optional `mode: ExecutionMode` parameter (`RUN` or `QUEUE`, default `RUN`) and an optional `priority: Int`.
- **Run only**: the mutation always runs synchronously. No `mode` or `priority` parameters.
- **Queue only**: the mutation always queues. Has `priority` but no `mode` parameter.

### Naming Convention

The mutation name is derived from the train's service interface name (or overridden via `[TraxMutation(Name = "...")]`):
1. Strip the `I` prefix
2. Strip the `Train` suffix
3. Use the result as the field name (camelCase)

For example, `IBanPlayerTrain` produces `banPlayer`.

### Example

Given a train annotated with `[TraxMutation]`:

```csharp
public record BanPlayerInput : IManifestProperties
{
    public required string PlayerId { get; init; }
    public required string Reason { get; init; }
}
```

The schema exposes:

```graphql
input BanPlayerInput {
  playerId: String!
  reason: String!
}

# Run synchronously (default mode)
mutation {
  dispatch {
    banPlayer(input: { playerId: "player-42", reason: "cheating" }) {
      externalId
      metadataId
      output { ... }
    }
  }
}

# Queue for async execution
mutation {
  dispatch {
    banPlayer(
      input: { playerId: "player-42", reason: "cheating" }
      mode: QUEUE
      priority: 10
    ) {
      externalId
      workQueueId
    }
  }
}
```

### Unified Response Type

Every dispatch mutation returns a single per-train response type with nullable fields. Which fields are populated depends on the execution mode:

```graphql
type BanPlayerResponse {
  externalId: String!       # always present
  metadataId: Long          # present for RUN, null for QUEUE
  output: BanPlayerOutput   # present for RUN (typed trains only), null for QUEUE
  workQueueId: Long         # present for QUEUE, null for RUN
}
```

| Field | Type | When Populated |
|-------|------|----------------|
| `externalId` | `String!` | Always present. RUN: the execution's external id. QUEUE: a 32-character hex id chosen at enqueue, stamped on the work queue entry, and carried by the run the scheduler later dispatches from it (see below) |
| `metadataId` | `Long` | RUN mode. Metadata ID of the completed execution |
| `output` | `{OutputType}` | RUN mode, only for trains with non-`Unit` output |
| `workQueueId` | `Long` | QUEUE mode. Database ID of the created WorkQueue entry |

A QUEUE mutation needs something to dispatch the entry. On a host whose store is in memory (`UseInMemory()` and no database provider) nothing can, so the request is refused with the error code `TRAX_QUEUE_UNAVAILABLE` and nothing is enqueued; use `mode: RUN` there. The train's `[TraxAuthorize]` requirements are checked first, so a caller the train refuses gets `TRAX_AUTHORIZATION`, as on any other host. A host on a database provider queues whether or not it runs the scheduler, since a scheduler on another host can dispatch the entry.

#### Following a queued run

A QUEUE mutation returns before any run exists, so it has no `metadataId`. Its `externalId` is the correlation key: the JobDispatcher copies it onto the execution (`trax.metadata.external_id`) it creates for the entry, so the run's lifecycle events, its `operations.executions` row and the work queue entry (`trax.work_queue.external_id`) all carry the same value. If a delivery to the runner fails and the entry is requeued (`MaxDispatchAttempts`), each attempt gets its own execution row with that same external id, and the failed attempts it requeues are recorded `Failed` by the dispatcher without a lifecycle event. The attempt that fails for good (its attempts exhausted, or its stored input unreadable) is the exception: no runner started it, so the dispatcher publishes its failure (`onTrainFailed` and a `FAILED` `onTrainStateChanged`, with `failureException` `DispatchFailed` and a fixed reason; the cause stays on the row). Otherwise only an attempt a runner actually started emits events. To watch a queued run from a client, subscribe before you queue and filter by `externalId`: see [Watching a queued run](/docs/sdk-reference/graphql-api/subscriptions#watching-a-queued-run).

A queued run that fails is not retried: retries belong to [manifests](/docs/scheduler/scheduling-options), and a work queue entry with no manifest runs once. An operator requeues a failed queued run with [`requeueExecution`](#requeueexecution) or the dashboard's Re-queue button.

The wrapper is named `{TrainName}Response` by default. If the train's output CLR class is also named `{TrainName}Response` (for example `IAddressValidationTrain` returning `AddressValidationResponse`), the wrapper falls back to `{TrainName}MutationResponse` so the schema can build without a name collision. The output type's name is the one HotChocolate gives it, its `[GraphQLName]` when it has one. Trains whose output type follows a different naming convention are unaffected. If the fallback name is itself taken, by the response type of a train named `{TrainName}Mutation`, the host refuses to start naming both trains; set `Name` on one of them.

### Run + Queue Mode (Default)

When no operations are specified (or both `GraphQLOperation.Run` and `GraphQLOperation.Queue` are passed), the mutation includes a `mode` parameter:

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `input` | `{TrainName}Input!` | Yes | N/A | Strongly-typed input matching the train's input record |
| `mode` | `ExecutionMode` | No | `RUN` | Whether to run synchronously (`RUN`) or queue for async execution (`QUEUE`) |
| `priority` | `Int` | No | `0` | Dispatch priority (0-31, higher runs first). Silently ignored for `RUN` mode. |

The `ExecutionMode` enum is automatically registered in the GraphQL schema when any train uses both Run and Queue operations:

```graphql
enum ExecutionMode {
  RUN
  QUEUE
}
```

#### Example: Run + Queue train with typed output

A train `ServiceTrain<LookupPlayerInput, LookupPlayerOutput>` annotated with `[TraxMutation]` (default, both modes) produces:

```graphql
type LookupPlayerResponse {
  externalId: String!
  metadataId: Long
  output: LookupPlayerOutput
  workQueueId: Long
}

type LookupPlayerOutput {
  playerId: String!
  rank: Int!
  wins: Int!
  losses: Int!
  rating: Int!
}

# Run synchronously (default)
mutation {
  dispatch {
    lookupPlayer(input: { playerId: "player-42" }) {
      externalId
      metadataId
      output {
        playerId
        rank
        wins
        losses
        rating
      }
    }
  }
}

# Queue for async execution
mutation {
  dispatch {
    lookupPlayer(
      input: { playerId: "player-42" }
      mode: QUEUE
      priority: 5
    ) {
      externalId
      workQueueId
    }
  }
}
```

The output type is automatically registered as a GraphQL `ObjectType` and deduplicated. If multiple trains share the same output type, only one GraphQL type is generated.

### Run-Only Mode

When `GraphQLOperation.Run` is the only operation passed (e.g. `[TraxMutation(GraphQLOperation.Run)]`), the mutation always runs synchronously. No `mode` or `priority` parameters are generated.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `{TrainName}Input!` | Yes | Strongly-typed input matching the train's input record |

The response type still uses the unified format, but `workQueueId` will always be `null`.

### Queue-Only Mode

When `GraphQLOperation.Queue` is the only operation passed (e.g. `[TraxMutation(GraphQLOperation.Queue)]`), the mutation always queues. No `mode` parameter is generated, but `priority` is available.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `input` | `{TrainName}Input!` | Yes | N/A | Strongly-typed input matching the train's input record |
| `priority` | `Int` | No | `0` | Dispatch priority (0-31, higher runs first) |

The response type still uses the unified format, but `metadataId` and `output` will always be `null`.

---

## Operations Mutations

The whole namespace sits behind the operations gate (`GateOperations`, `RequireAuthorization` or `AllowAnonymousOperations`; see [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)). Mutations that enqueue a train and input the caller chose (`requeueExecution`, `resumeExecution`, `workQueue.queueTrain`) also apply that train's `[TraxAuthorize]` requirements. Mutations that enqueue what a manifest fixed (`triggerManifest`, `triggerManifestDelayed`, `triggerGroup`, dead-letter requeues) are governed by the gate alone. See [Authorization: The Operations Surface](/docs/authorization#the-operations-surface).

The five mutations that take a manifest's `externalId` (`triggerManifest`, `triggerManifestDelayed`, `disableManifest`, `enableManifest`, `cancelManifest`) answer an id no manifest has with `success: false` and the message `Manifest '<externalId>' not found.`, and change nothing. It is a refusal, not a GraphQL error.

### triggerManifest

Triggers an immediate execution of a manifest, bypassing its normal schedule. A manifest holds at most one queued work queue entry, so when it already has one, nothing more is queued and that entry becomes the triggered run: it runs even if the manifest is disabled, and an entry due later (a retry waiting out its backoff) is brought forward to now. It goes through `IOperationsService.TriggerManifestAsync`, the call the dashboard's **Run Now** makes, and its message says which happened, naming the work queue entry, whose id is the response's `id`:

- `Manifest 'order-processing-daily' triggered: queued a new run (work queue entry 42), due now.`
- `Manifest 'order-processing-daily' already had a queued run (work queue entry 42); the trigger brought it forward, now due now, and queued nothing more.`
- `Manifest 'order-processing-daily' already had a queued run (work queue entry 42), due now; it now runs as the trigger and nothing more was queued.`
- `Manifest 'order-processing-daily' already had a queued run (work queue entry 42) that the dispatcher claimed as the trigger reached it, so it is already running; nothing more was queued.`

On a host whose store is in memory (`UseInMemory()` and no database provider) nothing dispatches the work queue, so the trigger answers `success: false` with that reason and queues nothing; the same holds for `triggerManifestDelayed`, `triggerGroup`, `triggerManifests`, `triggerGroups`, `workQueue.queueTrain`, `requeueExecution`, and the dead-letter requeues (`requeueDeadLetter`, `requeueDeadLetters`, `requeueAllDeadLetters`) ([Trax.Scheduler ADR 0019](https://github.com/TraxSharp/Trax/blob/main/Trax.Scheduler/docs/adr/0019-a-queued-run-is-refused-where-nothing-dispatches-it.md)).

```graphql
mutation {
  operations {
    triggerManifest(externalId: "order-processing-daily") {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |
| `askAfresh` | `Boolean!` | No | Default `false`. When `true` and the trigger releases a queued retry that would [replay the failed run's decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions), the run asks its deciders afresh instead |

**Returns**: `OperationResponse`. With `askAfresh: true`, a retry the dispatcher claimed before the
trigger reached it can no longer be changed. The mutation still succeeds, and its message says so:
`"Manifest 'order-processing-daily' triggered, but the dispatcher had already claimed its queued retry (work queue entry 42), so that run replays the decisions of execution 41 rather than asking its deciders afresh."`.

---

### triggerManifestDelayed

Triggers a manifest execution after a specified delay. When the manifest already has a queued entry, no second one is queued: that entry keeps its time if it is due sooner, and is brought forward to now plus `delay` otherwise. As with `triggerManifest`, the message says which happened and when the run is due, and `id` is the work queue entry.

```graphql
mutation {
  operations {
    triggerManifestDelayed(
      externalId: "order-processing-daily"
      delay: "PT5M"
    ) {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |
| `delay` | `TimeSpan!` | Yes | How long to wait before triggering, as an ISO-8601 duration (e.g. `"PT5M"` for 5 minutes) |
| `askAfresh` | `Boolean!` | No | Default `false`. As on `triggerManifest`, including the message when the dispatcher claimed the retry first |

**Returns**: `OperationResponse`

---

### disableManifest

Disables a manifest. Disabled manifests are skipped during scheduling cycles.

```graphql
mutation {
  operations {
    disableManifest(externalId: "order-processing-daily") {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |

**Returns**: `OperationResponse`

---

### enableManifest

Re-enables a previously disabled manifest.

```graphql
mutation {
  operations {
    enableManifest(externalId: "order-processing-daily") {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |

**Returns**: `OperationResponse`

---

### cancelManifest

Requests cancellation of every pending and running execution of a manifest. Sets `CancellationRequested` on each, which the run observes at its next junction boundary on any host; a run on the host that received the mutation is also cancelled at once.

```graphql
mutation {
  operations {
    cancelManifest(externalId: "order-processing-daily") {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |

**Returns**: `OperationResponse` (includes `count`, the number of executions marked for cancellation)

---

### triggerGroup

Triggers immediate execution of all enabled manifests in a group. A manifest that already has a queued work queue entry is not queued again and does not stop the others being queued; its entry is brought forward to now if it was due later. Every entry the trigger touches runs even if its manifest is disabled afterwards.

```graphql
mutation {
  operations {
    triggerGroup(groupId: 1) {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | The manifest group's database ID |

It goes through `IOperationsService.TriggerManifestGroupsAsync`, the call the dashboard's **Run Group** makes, with the one group.

**Returns**: `OperationResponse`. `count` is the number of manifests a new run was queued for; the message also counts the members that already had a queued run, which now runs as the trigger, for example `2 queued, 1 already queued (that entry now runs as the trigger) across 1 of 1 manifest group(s).` An unknown group id is counted as not found, with `count` 0.

---

### cancelGroup

Requests cancellation of every pending and running execution across all manifests in a group, by the same rule as [`cancelManifest`](#cancelmanifest).

```graphql
mutation {
  operations {
    cancelGroup(groupId: 1) {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | The manifest group's database ID |

**Returns**: `OperationResponse` (includes `count`, the number of executions marked for cancellation)

---

### triggerManifests

Triggers many manifests at once by database id, each as [`triggerManifest`](#triggermanifest) triggers one: an immediate run is queued, or a manifest's queued entry is brought forward to now when it already has one. It goes through `IOperationsService.TriggerManifestsAsync`, the call the dashboard's **Trigger Selected** (and **Trigger Selected (Ask Afresh)**) makes, so both count and note the same things.

```graphql
mutation {
  operations {
    triggerManifests(ids: [12, 13, 99999], askAfresh: false) {
      success
      matched
      queued
      alreadyQueued
      tooLateToAskAfresh
      skipped
      message
      notes { id message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest database ids (not external ids). A repeated id counts once |
| `askAfresh` | `Boolean!` | No (default `false`) | `true` makes a queued retry the trigger releases ask its deciders again instead of replaying the failed run's decisions. One the dispatcher had already claimed still replays, and is counted in `tooLateToAskAfresh` |

**Returns**: `BatchTriggerResponse`. An unknown id is skipped with a note and does not stop the rest. An empty list, or more than 1000 ids, returns `success: false` with every count `0` and triggers nothing. Each manifest is queued with its own save, so the cost grows with the batch: about 2 s for a full batch of 1000 new entries at the stress suite's scale, and 0.6 s when each already had one. A save that fails on the database is a GraphQL error rather than a `success: false`; the manifests queued before it stay queued, and sending the same batch again is safe, since a manifest holds at most one queued entry.

#### BatchTriggerResponse fields

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | `false` only when the batch was refused as given; then nothing was triggered |
| `matched` | `Int!` | How many of the ids named a manifest (for `triggerGroups`, a group) that exists and was triggered |
| `queued` | `Int!` | Manifests a new work queue entry was queued for |
| `alreadyQueued` | `Int!` | Manifests that already had a queued entry, so nothing more was queued: that entry became the triggered run, brought forward to now when it was due later. Also counts an entry the dispatcher claimed first, which is already running |
| `tooLateToAskAfresh` | `Int!` | Manifests triggered with `askAfresh: true` whose queued retry the dispatcher claimed first, so that run replays the failed run's decisions anyway. Each has a note naming the run it replays |
| `skipped` | `Int!` | Ids that named no manifest (or group). Each has a note |
| `message` | `String!` | One line for an operator |
| `notes` | `[BatchTriggerNote!]!` | One `{ id, message }` per id that did not get what the trigger asked for. Empty when every id was triggered as asked |

---

### triggerGroups

Triggers many manifest groups at once by id, each as [`triggerGroup`](#triggergroup) triggers one: every enabled member that runs on its own schedule is triggered, and `DEPENDENT` and `DORMANT_DEPENDENT` members are left to run after their parent. A disabled group is triggered too, as the single trigger triggers it, and holds its runs until it is enabled. It goes through `IOperationsService.TriggerManifestGroupsAsync`, the call the dashboard's groups page makes for **Trigger Selected**.

```graphql
mutation {
  operations {
    triggerGroups(ids: [1, 2]) { success matched queued alreadyQueued skipped message notes { id message } }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest group ids |

**Returns**: [`BatchTriggerResponse`](#batchtriggerresponse-fields). `matched` and `skipped` count group ids; `queued`, `alreadyQueued` and `tooLateToAskAfresh` count manifests. An empty list, or more than 1000 ids, returns `success: false` and triggers nothing. Its cost grows with the manifests the groups hold, one save each.

---

### cancelGroups

Requests cancellation of every pending and running execution of every manifest in the listed groups, by the rule [`cancelGroup`](#cancelgroup) applies to one, flagged in one update. It goes through `IOperationsService.CancelManifestGroupsAsync`, the call the dashboard's groups page makes for **Cancel Running**.

```graphql
mutation {
  operations {
    cancelGroups(ids: [1, 2]) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest group ids |

**Returns**: `OperationResponse`. `count` is the number of executions flagged, zero included, and `message` also says how many of the ids named a group. An empty list, or more than 1000 ids, returns `success: false` and flags nothing.

---

### cancelExecution

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

---

### cancelExecutions

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

**Returns**: `OperationResponse`. `count` is the number flagged, zero included. An empty list, or more than 1000 ids, returns `success: false` and flags nothing.

---

### setManifestsEnabled

Enables or disables many manifests at once. Only manifests whose flag differs are written. Backs the dashboard's bulk enable and disable.

```graphql
mutation {
  operations {
    setManifestsEnabled(ids: [3, 4, 5], enabled: false) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest database ids |
| `enabled` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of manifests changed, zero included. An empty list, or more than 1000 ids, returns `success: false` and changes nothing.

---

### setManifestsReplayDecisionsOnRetry

Sets whether retries of many manifests replay the decisions of the run they retry. Only manifests
whose flag differs are written. It calls `IOperationsService.SetManifestsReplayDecisionsOnRetryAsync`,
the call the dashboard makes. Turning it off also clears the replay link of each manifest's queued
entry, in the same transaction as the flag, so a retry waiting out its backoff asks afresh, and the
message counts them (`"2 queued retry(s) no longer replay a failed run's decisions."`). See
[Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions).

```graphql
mutation {
  operations {
    setManifestsReplayDecisionsOnRetry(ids: [3, 4], replay: false) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest database ids |
| `replay` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of manifests changed, zero included. An empty list, or more than 1000 ids, returns `success: false` and changes nothing.

---

### requeueExecution

Re-queues an execution: reads its train name + input from the metadata row and enqueues a
fresh work queue entry for the dispatcher (the GraphQL counterpart of the dashboard's Re-queue
button). Both call [`IOperationsService.RequeueExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#requeueexecutionasync),
so they refuse the same runs with the same messages. The enqueue goes through the same path as
[`queueTrain`](#queuetrain), so a caller who may not
run the train gets a GraphQL error with code `TRAX_AUTHORIZATION` (`"Not authorized."`) rather
than `success: false`.

The new run replays the decisions the execution recorded with
[`AddDecisionRecording`](/docs/sdk-reference/configuration/add-decision-recording), so it takes the
[tracks](/docs/core/decisions) the execution took instead of asking its deciders again. Among the API's mutations the replay link is set only here, to
the execution being re-queued, and only when it has decisions to replay (it recorded a decision,
or was itself a replaying requeue, so re-queueing a re-queue replays too); any other execution is
re-queued as an ordinary enqueue. `queueTrain` has no way to set it. A manifest's retry and a
dead-letter requeue are linked by the scheduler from its own checks, never from a caller (see
[Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions)).
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
the message rule [`queueTrain`](#queuetrain) describes, and an infrastructure failure is a masked GraphQL error, as for `queueTrain`. An enqueue reads a missing input as `{}`, so re-queueing it would re-run the train with
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

---

### resumeExecution

Resumes a failed or cancelled execution from a
[checkpoint](/docs/sdk-reference/train-methods/checkpoint) instead of running every step again: it
queues a run of the same train, with the input the execution recorded, that skips every step
before the resume point and starts from the state the checkpoint stored. With `from`, the run
resumes at that step, named by its node id as [`runGraph`](/docs/sdk-reference/graphql-api/queries#rungraph)
gives it; without it, after the execution's latest checkpoint. It is the GraphQL counterpart of the
dashboard's **Resume** and **Resume from here** buttons. All three call
[`IOperationsService.ResumeExecutionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#resumeexecutionasync),
so they refuse the same runs with the same messages. Experimental (`TRAXEXP003`).

A resume is a requeue in every check but where the run starts. It enqueues through the same path
as [`requeueExecution`](#requeueexecution), so a caller past the operations gate who may not run the
train gets a GraphQL error with code `TRAX_AUTHORIZATION` (`"Not authorized."`), and the new run
replays the execution's decisions as a requeue's would. It needs no role a requeue does not.

It is refused with `success: false`, the reason as the message, and nothing queued when:

- no execution has the id, or it is not `FAILED` or `CANCELLED` (`"Execution 100 is Completed; only a failed or cancelled run can be resumed."`);
- a state machine's step started it, since only that step receives its outcome;
- its saved input is missing, a placeholder or masked, for the reasons `requeueExecution` gives;
- a resume of it is already queued (`"A resume of execution 100 is already queued (WorkQueue 7); a run is resumed once at a time. Nothing was queued."`);
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

---

### cancelMachineInstance

Cancels a system-owned [state machine instance](/docs/statemachine/invoking-trains#operators)'s
live train run, and the instance then moves through its state's `OnCancelled` edge. It is the
GraphQL counterpart of the Cancel button on the dashboard's
[instance page](/docs/dashboard#state-machines); both call
[`IOperationsService.CancelMachineInstanceAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#state-machine-instances),
so they refuse the same instances with the same message.

A run still only queued is marked Cancelled and never starts. A run already dispatched has its
cancel requested: it stops at its next junction and ends Cancelled. The dispatcher claims an entry
and writes its run in one transaction, so a cancel racing the dispatcher ends exactly one of those
two ways. The outcome is then applied through the one conditional update every delivery makes,
so the instance moves once, whether this call, the run's lifecycle hook or the reconciler gets
there first.

Operators may cancel only `SYSTEM` instances. A user's draft is read-only to them: its run is
cancelled when the user leaves the state through one of the machine's own transitions. Nothing on
any surface creates or advances a system instance.

```graphql
mutation {
  operations {
    cancelMachineInstance(machine: "fulfilment", ownerKind: SYSTEM, id: "6f9619ff-8b86-d011-b42d-00c04fc964ff") {
      success
      outcome
      message
      state
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `machine` | `String!` | Yes | The machine's id. Blank is refused with `TRAX_INVALID_ARGUMENT` |
| `ownerKind` | `SnapshotOwnerKind!` | Yes | Who owns the instance; only `SYSTEM` can be cancelled |
| `id` | `UUID!` | Yes | The instance id |

**Returns**: `MachineInstanceCancelResponse`: `success`, `outcome`, `message` (the dashboard shows
the same text) and `state`, the state the instance entered when this call moved it.

| `outcome` | `success` | Meaning |
|-----------|-----------|---------|
| `MOVED` | `true` | The queued run is cancelled, and this host, which registers the machine, moved the instance through `OnCancelled`; `state` is the state it entered |
| `RUN_CANCELLED` | `true` | The queued run is cancelled; a host that registers the machine moves the instance (on Postgres the cancel wakes its reconciler) |
| `CANCEL_REQUESTED` | `true` | The run was dispatched; it stops at its next junction, and the instance moves when it ends |
| `USER_OWNED` | `false` | A user's draft. Nothing changes |
| `NOT_FOUND` | `false` | No system instance has this machine and id |
| `NO_LIVE_RUN` | `false` | The instance's state waits on no run, so there is nothing to cancel |
| `RUN_ENDED` | `false` | The run has already ended; its own outcome is being applied |

---

### updateManifest

Patches mutable settings on a single manifest. Each field on `input` is independent; a `null`
value leaves it unchanged. Set `clearTimeout: true` to remove the per-execution timeout.

It goes through `IOperationsService.UpdateManifestAsync`, which runs every check before writing a
field. A value the scheduler could not use is refused with `success: false`, a message naming the
field, and nothing saved:

- `timeoutSeconds` of 0 or less (a zero timeout would cancel every run at once; use `clearTimeout`),
- `intervalSeconds` of 0 or less, `maxRetries` below 0, and `priority` outside 0 to 31 (the work
  queue's range),
- `scheduleType: CRON` with no `cronExpression` on the input or the manifest, or a `cronExpression`
  the scheduler cannot use: not 5 or 6 space-separated fields, a field out of range
  (`99 * * * *`), or an expression that never fires (`0 0 30 2 *`),
- `scheduleType: INTERVAL` with no `intervalSeconds` on the input or the manifest,
- a switch to `ONCE`, `DEPENDENT` or `DORMANT_DEPENDENT`, which need a time or a parent manifest this
  input cannot give. Schedule those from code.

The schedule is checked only when the input changes it (`scheduleType`, `cronExpression` or
`intervalSeconds`), so a manifest whose stored schedule is unusable can still be disabled.

```graphql
mutation {
  operations {
    updateManifest(id: 42, input: {
      isEnabled: false
      maxRetries: 5
      priority: 10
      scheduleType: CRON
      cronExpression: "0 3 * * *"
    }) { success message }
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `isEnabled` | `Boolean` | Enable/disable the manifest |
| `maxRetries` | `Int` | Retry budget |
| `priority` | `Int` | Dispatch priority, 0 to 31 |
| `timeoutSeconds` / `clearTimeout` | `Int` / `Boolean` | Per-execution timeout; `clearTimeout: true` removes it |
| `scheduleType` | `ScheduleType` | Schedule type |
| `cronExpression` | `String` | Cron expression |
| `intervalSeconds` | `Int` | Interval |

**Returns**: `OperationResponse` (`success: false` when the manifest id does not exist or a value is refused; on success `id` is the manifest's).

---

### setEffectEnabled

Turns an observational effect on or off in the API process, through `IEffectSettingsService.SetEffectEnabled` in Trax.Scheduler, the call the dashboard's [Effects page](/docs/dashboard#effects-page) makes. The change is in memory: it does not reach the scheduler or worker processes where trains usually run, and a restart restores the configured state.

```graphql
mutation {
  operations {
    setEffectEnabled(
      fullName: "Trax.Effect.Provider.Json.Services.JsonEffectFactory.JsonEffectProviderFactory"
      enabled: false
    ) {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `fullName` | `String!` | Yes | The effect factory's full type name, as [`operations.effects`](/docs/sdk-reference/graphql-api/queries#effects) reports it in `fullName`. Matched exactly. |
| `enabled` | `Boolean!` | Yes | `true` to turn the effect on, `false` to turn it off |

**Returns**: `OperationResponse`. `success` is false, and nothing changes, when no effect has that name, the effect was registered as not toggleable, or the host registers no effect registry. On success `count` is 1.

---

### configureEffect

Writes settings of a configurable effect in the API process, through `IEffectSettingsService.ConfigureEffect` in Trax.Scheduler, the call the dashboard's Configure dialog makes. [`effects`](/docs/sdk-reference/graphql-api/queries#effects) lists each effect's `fields`: their names, how each is edited and what to type. Send only the settings you change; a setting not listed is not written, so a change made elsewhere to it is kept.

```graphql
mutation {
  operations {
    configureEffect(
      fullName: "Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory.ParameterEffectProviderFactory"
      values: [
        { name: "SaveOutputs", value: "false" }
        { name: "MaxParameterBytes", value: null }
      ]
    ) {
      success
      count
      message
      errors { field message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `fullName` | `String!` | Yes | The effect factory's full type name, as `effects` reports it. Matched exactly |
| `values` | `[EffectSettingValueInput!]!` | Yes | 1 to 1000 `{ name: String!, value: String }`, each setting at most once |

Each `value` is text read as the setting's type: numbers in invariant culture with `.` for the decimal point and no thousands separators, a date or time with no offset as UTC, a boolean as `true` or `false`, an enum by member name. Null or blank is no value for a setting that accepts null, the empty string for a text setting that does not, and refused for anything else. A `SET_IN_CODE` setting cannot be written. A `[TraxSensitive]` setting can be written, though it is never read back.

It is all or nothing: every value is read and checked against the setting's validation attributes before any is written, and when one is refused, or a setter throws part way, nothing is written.

**Returns**: `ConfigureEffectResponse`:

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether the settings were written |
| `count` | `Int!` | How many settings were written; `0` on failure |
| `message` | `String!` | One line for an operator |
| `errors` | `[EffectSettingError!]!` | `{ field, message }` for each refused setting: no such setting, set in code, given twice, not readable as its type, or failing its validation. Empty on success, and on a failure that is not one setting's, such as an unknown effect or one with no settings |

The change is in memory and applies to the next run in this process. It does not reach the scheduler or worker processes where trains usually run, and a restart restores the configured settings.

---

### config (nested namespace)

The `operations.config` namespace patches scheduler runtime settings. A save writes only the fields it sets to the persisted `trax.scheduler_config` row, so it never rewrites a setting it did not name, and applies them to the host that received it at once. Every running scheduler host reads the row every few seconds and applies a new or changed one without a restart, so a save made on an API-only host, or on one of several scheduler hosts, reaches all of them. A scheduler applies a change from its next polling cycle, including a new polling or cleanup interval; `localWorkerCount` is the exception and applies when the worker pool next starts. The row also survives restarts: each scheduler applies it at startup over the settings configured in code.

The row records which settings a save named, in its `overrides`. A setting no save has named is not stored, so each scheduler keeps the value configured in code for it, and a later change in code applies to it. That is why any host can make a save, the first one included, whether or not it runs the scheduler: an API-only host built with `AddTraxJobRunner()` never has to supply values for settings it did not name. Two hosts making the first save at the same time both succeed; the one that loses the race applies its patch to the row the other created.

Which fields a save stores depends on the host. A field whose setting the row already names is stored when it differs from the stored value. For a setting the row does not name, a scheduler host stores the field only when it differs from the value that host runs with, so a save from the dashboard on a scheduler host leaves unchanged fields out. A host that does not run the scheduler cannot know that value, so it stores every field the patch sets, and each field it sends becomes a saved value that replaces the code value on every scheduler. `count` counts the fields stored.

A stored value takes precedence over the value in code until it is changed or the row is deleted, and deleting the row returns every running scheduler to its configured settings. Each scheduler logs a warning when a saved value replaces a different value configured in code, because a deploy that changes that setting in code then has no effect. The two dead-letter purge settings are the exception to "the saved value wins": when code states them too, the purge runs only if both `autoPurgeDeadLetters` values allow it, and the longer of the two `deadLetterRetentionPeriod` values applies (see [Dead Letter Auto-Purge](/docs/scheduler/dead-letters-and-cleanup#dead-letter-auto-purge)).

A scheduler that cannot read the row when it starts (the database is briefly unreachable, or the table is not migrated yet) logs a warning, runs with its code values, and applies the row at its first successful read.

#### updateScheduler

Patches one or more fields. Fields left out of `input` are unchanged. The persisted row's `updatedAt` only moves on real changes (no-op patches are ignored at the DB layer).

```graphql
mutation {
  operations {
    config {
      updateScheduler(input: {
        defaultMaxRetries: 5
        defaultJobTimeout: "00:30:00"
      }) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `UpdateSchedulerConfigInput!` | Yes | Patch payload (fields below) |

#### setLogLevels

Sets the level each listed log category filters at in the API process, through `ILogLevelService.SetLogLevels` in Trax.Scheduler, the call the dashboard's server settings make. [`config.logLevels`](/docs/sdk-reference/graphql-api/queries#environmentname-version-and-loglevels) reads the result.

```graphql
mutation {
  operations {
    config {
      setLogLevels(levels: [
        { category: "Default", level: WARNING }
        { category: "Trax", level: DEBUG }
      ]) { success count notApplied message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `levels` | `[LogLevelSettingInput!]!` | Yes | 1 to 1000 `{ category: String!, level: LogLevel! }`. `category` is a category configured under `Logging:LogLevel` (or changed here before), case ignored; `level` is `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL` or `NONE`. When a category is listed twice, the last level wins |

**Returns**: `SetLogLevelsResponse` with `success: Boolean!`, `count: Int!` (categories set), `notApplied: [String!]!` and `message: String!`. A list naming a category that is not configured, or an empty list, is refused whole and nothing changes. `notApplied` lists categories set whose loggers still filter at another level, which happens when the host sets the logger filter itself after Trax.

The change is applied to the host's logger filter options over every configuration source and lasts until the process restarts; nothing is persisted, and it does not reach the scheduler or worker processes. A host that does not call `AddScheduler` registers no `ILogLevelService`, and there `setLogLevels` returns `success: false`.

#### UpdateSchedulerConfigInput fields

Every field defaults to `null` and means "no change". To clear `maxActiveJobs` (set to "no per-group cap") or `localWorkerCount` (reset to `Environment.ProcessorCount`), set the corresponding `clear*` flag to `true`.

| Field | Type | Description |
|-------|------|-------------|
| `manifestManagerEnabled` | `Boolean` | |
| `jobDispatcherEnabled` | `Boolean` | |
| `manifestManagerPollingInterval` | `TimeSpan` | 1 second to 30 days |
| `jobDispatcherPollingInterval` | `TimeSpan` | 1 second to 30 days |
| `maxActiveJobs` | `Int` | At least 1 |
| `clearMaxActiveJobs` | `Boolean` | When `true`, sets `maxActiveJobs` to null |
| `defaultMaxRetries` | `Int` | Zero or more |
| `failureCountWindow` | `TimeSpan` | 1 second to ten years. How far back failed runs count toward retry backoff and `MaxRetries` |
| `defaultRetryDelay` | `TimeSpan` | Zero to ten years |
| `retryBackoffMultiplier` | `Float` | At least 1 |
| `maxRetryDelay` | `TimeSpan` | Zero to ten years |
| `defaultJobTimeout` | `TimeSpan` | 1 second to ten years |
| `stalePendingTimeout` | `TimeSpan` | 1 second to ten years |
| `recoverStuckJobsOnStartup` | `Boolean` | |
| `deadLetterRetentionPeriod` | `TimeSpan` | Zero to ten years |
| `autoPurgeDeadLetters` | `Boolean` | |
| `localWorkerCount` | `Int` | 1 to 256. Ignored when this process runs no local worker pool (see [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers)). Applies when the worker pool next starts |
| `clearLocalWorkerCount` | `Boolean` | Resets `localWorkerCount` to `Environment.ProcessorCount` |
| `metadataCleanupInterval` | `TimeSpan` | 1 second to 30 days. Ignored when metadata cleanup is not configured |
| `metadataCleanupRetention` | `TimeSpan` | 1 second to ten years. Ignored when metadata cleanup is not configured |

**Returns**: `OperationResponse`. `count` is the number of fields actually changed (zero if every supplied value already matched). A value outside its range makes `success` `false`, with a `message` naming each offending field, and nothing in the patch is applied or persisted; so does a first save on a host that does not run the scheduler. The ranges are what the scheduler can run with: an interval is the wait between two polling cycles, which a timer caps at about 49 days, and polling the database more often than once a second is load rather than responsiveness. When a scheduler applies the row, a persisted value outside its range (from a row written before these checks, or edited by hand) is skipped with a warning, and the configured value stays in effect.

---

### manifestGroups (nested namespace)

The `operations.manifestGroups` namespace patches mutable fields on a manifest group and enables or disables groups in bulk. The dashboard calls the same underlying service, so a save from either surface produces an identical write.

#### updateManifestGroup

Patches one or more fields on a manifest group. Fields left out of `input` are unchanged; `updatedAt` is bumped only when at least one field actually changed.

```graphql
mutation {
  operations {
    manifestGroups {
      updateManifestGroup(id: 7, input: {
        priority: 5
        isEnabled: false
      }) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The manifest group's database ID |
| `input` | `UpdateManifestGroupInput!` | Yes | Patch payload (fields below) |

#### UpdateManifestGroupInput fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `maxActiveJobs` | `Int` | `null` | New per-group concurrency limit, at least 1. `null` means "no change". To clear the limit, use `clearMaxActiveJobs` instead |
| `clearMaxActiveJobs` | `Boolean` | `false` | When `true`, sets `maxActiveJobs` to `null` (removes the per-group limit). Takes precedence if both this and `maxActiveJobs` are set |
| `priority` | `Int` | `null` | New priority, 0 to 31. `null` = no change |
| `isEnabled` | `Boolean` | `null` | Whether the group is active. `null` = no change |

**Returns**: `OperationResponse`. On success, `count` is the number of fields actually changed (zero if every supplied value already matched the persisted row). On failure (the group is not found, or a value is out of range), `success` is `false`, `message` explains, and no field is written.

#### setManifestGroupsEnabled

Enables or disables the listed groups. Only groups whose flag differs are written, with `updatedAt` bumped.

```graphql
mutation {
  operations {
    manifestGroups {
      setManifestGroupsEnabled(ids: [1, 2], enabled: false) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest group ids |
| `enabled` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of groups changed, zero included. An empty list, or more than 1000 ids, returns `success: false` and changes nothing: an empty list never means "every group".

#### setAllManifestGroupsEnabled

Enables or disables every manifest group. It is a field of its own so that "all" is something a caller asks for by name.

```graphql
mutation {
  operations {
    manifestGroups {
      setAllManifestGroupsEnabled(enabled: true) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `enabled` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of groups changed, zero included.

---

### workQueue (nested namespace)

The `operations.workQueue` namespace lets the dashboard (and other API clients) put work into the queue and cancel it. Reads live under [operations.workQueue in queries.md](/docs/sdk-reference/graphql-api/queries#workqueue-nested-under-operations).

#### queueTrain

Creates a new work queue entry. The dispatcher picks it up on its next poll. An unknown `trainName`, malformed or oversized `inputJson`, or JSON that deserializes to `null` returns `OperationResponse(success: false, message: ...)` and inserts nothing. For most trains nothing is written until every check has passed. A train that sets [`DeferQueuePromotion`](/docs/core/trains-and-junctions#making-the-side-effect-durable) is the exception: its entry is committed unconfirmed (never dispatched) before its `OnQueue` hook runs, and removed again if the hook throws, so a refused enqueue of such a train does briefly write a row.

Upgrading from a version where this mutation wrote the row itself: it now authorizes, fires `OnQueue` and stamps the subject key. See [Enqueue and Outcome Changes](/docs/migration-guides/enqueue-and-outcome-changes).

The entry is created through [`ITrainExecutionService.QueueAsync`](/docs/sdk-reference/mediator-api/train-execution#queueasync), so the train's `[TraxAuthorize]` requirements apply on top of the operations gate. Authorization runs before `inputJson` is read: a caller who may not run the train gets a GraphQL error with code `TRAX_AUTHORIZATION` and message `"Not authorized."`, not `success: false`, even when the input is malformed, and nothing is inserted. See [Authorization: The Operations Surface](/docs/authorization#the-operations-surface).

Four kinds of exception propagate out of the mutation rather than becoming `success: false`. Authorization (an `UnauthorizedAccessException`, which `TrainAuthorizationException` is) surfaces as the `TRAX_AUTHORIZATION` error above. Cancellation of the request ends it. An infrastructure failure, meaning a database, EF Core, network, I/O or timeout exception anywhere in the exception's chain (a `DbException` such as `NpgsqlException`, `DbUpdateException`, `TimeoutException`, `SocketException`, `HttpRequestException` or `IOException`), is logged on the server and arrives as a GraphQL error with HotChocolate's masked `"Unexpected Execution Error"` message, so nothing about the server reaches the caller. That includes a data-layer exception caused by the train's own `OnQueue` hook; a hook that means to refuse throws its own exception. The mediator's `TrainAuthorizationNotConfiguredException`, for a `[TraxAuthorize]` train on a host with no `ITrainAuthorizationService` registered, is a host misconfiguration, so it is logged and masked the same way.

Every other exception from the enqueue is a refusal and becomes `success: false`: invalid JSON as `"Invalid InputJson: "` followed by the parser's message, an oversized input as the generic `"The train input failed validation."` (neither the cap nor the input's size is echoed), and anything else as a refusal. That last group covers the train's `OnQueue` hook throwing, `QueueSubjectKey` throwing or returning an empty key, one that is only whitespace, or one longer than 512 Unicode characters, and a deferred entry being cancelled before it was confirmed (in which case the hook's side-effect may already have landed). A refusal's message is `"The enqueue was refused: "` followed by the exception's message only when that message was written for the caller: a plain `TrainException` (not a type derived from it), whose message is the train author's, or the mediator's `QueuedWorkCancelledException` and `QueueHookTimeoutException`. For any other exception it is the fixed `"The enqueue was refused."`, and the exception is logged at Warning on the server. A hook that refuses with a reason the caller should read throws `TrainException`. `Trax.Scheduler/docs/adr/0004` records the split.

```graphql
mutation {
  operations {
    workQueue {
      queueTrain(input: {
        trainName: "MyApp.Trains.Billing.IChargeCustomerTrain"
        inputJson: "{\"attackerId\":\"player-1\",\"defenderId\":\"player-2\"}"
        priority: 10
      }) {
        success
        count
        message
      }
    }
  }
}
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `trainName` | `String!` | Yes | N/A | Train interface FullName, the `fullName` that [`operations.trains`](/docs/sdk-reference/graphql-api/queries#trains) returns (not its `serviceTypeName`, which is a display name) |
| `inputJson` | `String` | No | `null` | JSON payload that deserializes to the train's input type. `null` or blank is read as `{}`, so a train whose input needs no values can be queued without one; an input type that needs values (a positional record whose parameters have no defaults, or a `required` member) is refused. The JSON literal `null` is refused |
| `priority` | `Int` | No | `0` | Dispatch priority 0-31. Values outside that range are clamped |
| `scheduledAt` | `DateTime` | No | `null` | Earliest UTC time the entry should be picked up. Null means dispatch immediately |

**Returns**: `OperationResponse`. On success, `count` is `1` and `id` is the new work queue entry's id, which `message` also names.

#### runTrain

Runs a train now, through the same `IOperationsService.RunTrainAsync` call as the dashboard's Run dialog. The run's `PENDING` execution row is written and handed straight to the job submitter the train is routed to (its `[TraxRemote]` or builder route, otherwise the host's default), so it skips the work queue: dispatch ordering, group limits and the subject lock do not apply. Use [`queueTrain`](#queuetrain) when they should.

What the caller sees follows the operations payload convention:

- **Refused**: `success: false` with a `message`, and no execution row is written. An unknown `trainName`, invalid JSON (`"Invalid InputJson: "` and the parser's message), an oversized input (the generic `"The train input failed validation."`), or a refusal the train itself makes.
- **Not allowed**: the train's `[TraxAuthorize]` requirements apply on top of the operations gate and are checked before `inputJson` is read. A caller who may not run the train gets a GraphQL error with code `TRAX_AUTHORIZATION` and message `"Not authorized."`, even when the input is malformed.
- **Server failure**: when the submitter fails, the execution row is marked `FAILED` with that exception and the caller gets HotChocolate's masked `"Unexpected Execution Error"`. A database failure writing the row is reported the same way.

```graphql
mutation {
  operations {
    workQueue {
      runTrain(input: {
        trainName: "MyApp.Trains.Billing.IChargeCustomerTrain"
        inputJson: "{\"attackerId\":\"player-1\",\"defenderId\":\"player-2\"}"
      }) {
        success
        id
        message
      }
    }
  }
}
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `trainName` | `String!` | Yes | N/A | Train interface FullName, as for [`queueTrain`](#queuetrain) |
| `inputJson` | `String` | No | `null` | JSON payload that deserializes to the train's input type. Property names match whatever their case and a property given twice is refused. `null` or blank is read as `{}` |

**Returns**: `OperationResponse`. On success, `id` is the execution's metadata id (pass it to `operations.execution` or `executionDetail`), not a work queue id, and `count` is `1`.

A host that exposes the operations mutations must register an `IJobSubmitter`, or it refuses to start; see [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql).

#### cancelWorkQueueEntry

Cancels a queued entry. Only entries with `status: QUEUED` can be cancelled. Already-dispatched or already-cancelled entries return `OperationResponse(success: false, ...)` without modifying the row.

```graphql
mutation {
  operations {
    workQueue {
      cancelWorkQueueEntry(id: 1234) { success message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The work queue entry's database ID |

**Returns**: `OperationResponse`.

#### cancelWorkQueueEntries

Cancels many queued entries in one round-trip (a single set-based `UPDATE` through the same
`IOperationsService.CancelWorkQueueEntriesAsync` call as the dashboard). Only entries still
`QUEUED` are affected; already-dispatched or already-cancelled ids in the list are skipped.
Backs the dashboard's bulk-cancel selection.

```graphql
mutation {
  operations {
    workQueue {
      cancelWorkQueueEntries(ids: [1234, 1235, 1236]) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 work queue entry ids to cancel |

**Returns**: `OperationResponse`. `count` is the number actually cancelled, zero included. An empty list, or more than 1000 ids, returns `success: false` (`"No ids were given."` for an empty one) and cancels nothing.

---

### deadLetters (nested namespace)

The `operations.deadLetters` namespace exposes dead-letter requeue and acknowledge mutations: `requeueDeadLetter`, `acknowledgeDeadLetter`, batch variants (`requeueDeadLetters`, `acknowledgeDeadLetters`), and "all" variants (`requeueAllDeadLetters`, `acknowledgeAllDeadLetters`). The batch variants take 1 to 1000 ids; an empty or longer list returns a count of zero with the reason in `message` and changes nothing. An acknowledgement `note` is at most 1,000 characters; a longer one is refused (`success: false`, or `count: 0` on the batch and "all" variants) and nothing changes. The three requeues take an optional `askAfresh: Boolean!` (default `false`): left false, a requeued run [replays the failed run's decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions) when that is sound; true, it asks its deciders afresh.

`requeueAllDeadLetters` does not hold the request for the whole backlog. It starts the requeue as a background job on the node that received it and returns that job at once, as a `DeadLetterRequeueJob`; read it again with the `requeueAllJob(id)` query (see [Queries](/docs/sdk-reference/graphql-api/queries)) until `status` is no longer `RUNNING`. The job is not tied to the request: it finishes after the client goes away, and HotChocolate's execution timeout does not apply to it. Only the node shutting down stops it, between pages, and the job ends `CANCELED`. The job is the scheduler's [`IDeadLetterRequeueJobs`](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs), which the dashboard's **Requeue All** starts too, so a requeue-all started from either surface is the one the other sees running.

```graphql
mutation {
  operations {
    deadLetters {
      requeueDeadLetter(id: 42) { success workQueueId message }
      requeueAllDeadLetters { id status awaitingAtStart started }
    }
  }
}
```

| Mutation | Arguments | Returns |
|----------|-----------|---------|
| `requeueDeadLetter` | `id: Long!`, `askAfresh: Boolean! = false` | `DeadLetterOperationResult` |
| `acknowledgeDeadLetter` | `id: Long!`, `note: String!` | `DeadLetterOperationResult` |
| `requeueDeadLetters` | `ids: [Long!]!`, `askAfresh: Boolean! = false` | `BatchDeadLetterResult` |
| `acknowledgeDeadLetters` | `ids: [Long!]!`, `note: String!` | `BatchDeadLetterResult` |
| `requeueAllDeadLetters` | `askAfresh: Boolean! = false` | `DeadLetterRequeueJob` |
| `acknowledgeAllDeadLetters` | `note: String!` | `BatchDeadLetterResult` |

| Type | Fields |
|------|--------|
| `DeadLetterOperationResult` | `success: Boolean!`, `workQueueId: Long` (the entry a requeue queued; `null` for an acknowledge or a refusal), `message: String!` |
| `BatchDeadLetterResult` | `count: Int!` (dead letters resolved), `message: String!` (also counts the folded and skipped ones) |
| `DeadLetterRequeueJob` | The requeue-all job, below |

A requeue or acknowledge that cannot be done (the dead letter is not awaiting intervention, or its manifest already has a queued entry) returns `success: false` and the reason in `message`, not a GraphQL error. These types are not `OperationResponse`.

`DeadLetterRequeueJob`:

| Field | Type | Description |
|-------|------|-------------|
| `id` | `UUID!` | The job, for `requeueAllJob(id)` on the same node |
| `status` | `DeadLetterRequeueJobStatus!` | `RUNNING`, `SUCCEEDED`, `FAILED` or `CANCELED` |
| `awaitingAtStart` | `Int!` | Dead letters awaiting intervention when it started |
| `startedAt` / `finishedAt` | `DateTime!` / `DateTime` | When it started, and when it stopped (`null` while running) |
| `count` | `Int` | Dead letters it requeued, once `SUCCEEDED`; `null` before |
| `processed` | `Int!` | Dead letters it has requeued so far, updated as each page commits; with `awaitingAtStart`, how far a running job has got. It can pass `awaitingAtStart` when dead letters arrive while it runs |
| `message` | `String!` | Where it is, or how it ended. A `FAILED` job's message says only that the server failed; the detail is in the server's log |
| `started` | `Boolean!` | `false` when a requeue-all was already running on this node: no second one was started, and this is the running one |

One requeue-all runs per node at a time. A job lives in the memory of the node that started it, so another node, or the same node after a restart, answers `requeueAllJob` with `null`. Nothing is lost by that: a requeued dead letter no longer awaits intervention, so a requeue that stopped part-way left what it finished requeued, and running `requeueAllDeadLetters` again requeues the rest. A finished job can be read for 24 hours, and a node keeps at most 100.

See [scheduler/dead-letters-and-cleanup](/docs/scheduler/dead-letters-and-cleanup) for full details and examples.

---

## OperationResponse

Shared response type for operations mutations.

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether the operation succeeded |
| `count` | `Int` | Number of affected records, for a mutation that acts on a set or patches fields; `null` otherwise |
| `id` | `Long` | The one row the operation acted on, when there is one: the work queue entry `queueTrain`, `requeueExecution` and `resumeExecution` created, the execution `runTrain` started, the group `updateManifestGroup` patched. `null` otherwise |
| `message` | `String` | Human-readable status message |
