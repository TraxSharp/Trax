---
layout: default
title: Mutations
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
| `externalId` | `String!` | Always present. Identifies the execution or work queue entry |
| `metadataId` | `Long` | RUN mode. Metadata ID of the completed execution |
| `output` | `{OutputType}` | RUN mode, only for trains with non-`Unit` output |
| `workQueueId` | `Long` | QUEUE mode. Database ID of the created WorkQueue entry |

The wrapper is named `{TrainName}Response` by default. If the train's output CLR class is also named `{TrainName}Response` (for example `IAddressValidationTrain` returning `AddressValidationResponse`), the wrapper falls back to `{TrainName}MutationResponse` so the schema can build without a name collision. Trains whose output type follows a different naming convention are unaffected.

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

The whole namespace sits behind the operations gate (`GateOperations`, `RequireAuthorization` or `AllowAnonymousOperations`; see [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)). Mutations that enqueue a train and input the caller chose (`requeueExecution`, `workQueue.queueTrain`) also apply that train's `[TraxAuthorize]` requirements. Mutations that enqueue what a manifest fixed (`triggerManifest`, `triggerManifestDelayed`, `triggerGroup`, dead-letter requeues) are governed by the gate alone. See [Authorization: The Operations Surface](/docs/authorization#the-operations-surface).

### triggerManifest

Triggers an immediate execution of a manifest, bypassing its normal schedule. A manifest holds at most one queued work queue entry, so when it already has one, that entry runs it and nothing more is queued; the mutation still succeeds.

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

**Returns**: `OperationResponse`

---

### triggerManifestDelayed

Triggers a manifest execution after a specified delay.

```graphql
mutation {
  operations {
    triggerManifestDelayed(
      externalId: "order-processing-daily"
      delay: "00:05:00"
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
| `delay` | `TimeSpan!` | Yes | How long to wait before triggering (e.g. `"00:05:00"` for 5 minutes) |

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

Requests cancellation of all running executions for a manifest. Sets `CancellationRequested` on active metadata records so the next cancellation-token check aborts execution.

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

Triggers immediate execution of all enabled manifests in a group. A manifest that already has a queued work queue entry is skipped, and does not stop the others being queued.

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

**Returns**: `OperationResponse` (includes `count`, the number of manifests queued, which leaves out those skipped as already queued; the server logs the skipped count)

---

### cancelGroup

Requests cancellation of all running executions across all manifests in a group.

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

### cancelExecution

Requests cancellation of a single execution by metadata id. Sets the durable
`cancel_requested` flag on the row (only when it is still `PENDING` or `IN_PROGRESS`); an
in-process runner observes it and transitions the train to `CANCELLED`.

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

**Returns**: `OperationResponse`. `count` is `1` when flagged, `0` when the execution is missing or already terminal.

---

### requeueExecution

Re-queues an execution: reads its train name + input from the metadata row and enqueues a
fresh work queue entry for the dispatcher (the GraphQL counterpart of the dashboard's Re-queue
button). It goes through the same path as [`queueTrain`](#queuetrain), so a caller who may not
run the train gets a GraphQL error with code `TRAX_AUTHORIZATION` (`"Not authorized."`) rather
than `success: false`.

An execution with no saved input is refused with `success: false` and a message saying inputs are
saved only when [`SaveTrainParameters()`](/docs/sdk-reference/configuration/save-train-parameters)
is on. So is one whose input was too large to save in full and was stored as the truncation
placeholder (`{"_truncated": true, ...}`). Enqueue refusals (a throwing `OnQueue`, an unusable
subject key, a deferred entry cancelled before confirmation) come back as `success: false` with
`"The enqueue was refused: ..."`, and an infrastructure failure is a masked GraphQL error, as for `queueTrain`. An enqueue reads a missing input as `{}`, so re-queueing it would re-run the train with
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

**Returns**: `OperationResponse`.

---

### updateManifest

Patches mutable settings on a single manifest. Each field on `input` is independent; a `null`
value leaves it unchanged. Set `clearTimeout: true` to remove the per-execution timeout.

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
| `priority` | `Int` | Dispatch priority |
| `timeoutSeconds` / `clearTimeout` | `Int` / `Boolean` | Per-execution timeout; `clearTimeout: true` removes it |
| `scheduleType` | `ScheduleType` | Schedule type |
| `cronExpression` | `String` | Cron expression |
| `intervalSeconds` | `Int` | Interval |

**Returns**: `OperationResponse` (`success: false` when the manifest id does not exist).

---

### config (nested namespace)

The `operations.config` namespace patches scheduler runtime settings. A save writes only the fields it sets to the persisted `trax.scheduler_config` row, so it never rewrites a setting it did not name, and applies them to the host that received it at once. Every running scheduler host reads the row every few seconds and applies a new or changed one without a restart, so a save made on an API-only host, or on one of several scheduler hosts, reaches all of them. A scheduler applies a change from its next polling cycle, including a new polling or cleanup interval; `localWorkerCount` is the exception and applies when the worker pool next starts. The row also survives restarts: each scheduler applies it at startup over the settings configured in code.

The row stores every setting, so the first save, which creates it, records the saving host's values for the settings it does not name. A host that does not run the scheduler (an API-only host built with `AddTraxJobRunner()`) cannot know those values, so there the first save is refused with a message saying so; make it on a host that calls `AddScheduler`. Once the row exists, a stored value takes precedence over the value in code until the row is changed or deleted, and deleting the row returns every running scheduler to its configured settings.

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
| `defaultRetryDelay` | `TimeSpan` | Zero to ten years |
| `retryBackoffMultiplier` | `Float` | At least 1 |
| `maxRetryDelay` | `TimeSpan` | Zero to ten years |
| `defaultJobTimeout` | `TimeSpan` | 1 second to ten years |
| `stalePendingTimeout` | `TimeSpan` | 1 second to ten years |
| `recoverStuckJobsOnStartup` | `Boolean` | |
| `deadLetterRetentionPeriod` | `TimeSpan` | Zero to ten years |
| `autoPurgeDeadLetters` | `Boolean` | |
| `localWorkerCount` | `Int` | 1 to 256. Ignored when `UseLocalWorkers()` is not configured. Applies when the worker pool next starts |
| `clearLocalWorkerCount` | `Boolean` | Resets `localWorkerCount` to `Environment.ProcessorCount` |
| `metadataCleanupInterval` | `TimeSpan` | 1 second to 30 days. Ignored when metadata cleanup is not configured |
| `metadataCleanupRetention` | `TimeSpan` | 1 second to ten years. Ignored when metadata cleanup is not configured |

**Returns**: `OperationResponse`. `count` is the number of fields actually changed (zero if every supplied value already matched). A value outside its range makes `success` `false`, with a `message` naming each offending field, and nothing in the patch is applied or persisted; so does a first save on a host that does not run the scheduler. The ranges are what the scheduler can run with: an interval is the wait between two polling cycles, which a timer caps at about 49 days, and polling the database more often than once a second is load rather than responsiveness. When a scheduler applies the row, a persisted value outside its range (from a row written before these checks, or edited by hand) is skipped with a warning, and the configured value stays in effect.

---

### manifestGroups (nested namespace)

The `operations.manifestGroups` namespace patches mutable fields on a manifest group. The dashboard's group settings panel calls the same underlying service, so a save from either surface produces an identical write.

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

---

### workQueue (nested namespace)

The `operations.workQueue` namespace lets the dashboard (and other API clients) put work into the queue and cancel it. Reads live under [operations.workQueue in queries.md](/docs/sdk-reference/graphql-api/queries#workqueue-nested-under-operations).

#### queueTrain

Creates a new work queue entry. The dispatcher picks it up on its next poll. An unknown `trainName`, malformed or oversized `inputJson`, or JSON that deserializes to `null` returns `OperationResponse(success: false, message: ...)` and inserts nothing. For most trains nothing is written until every check has passed. A train that sets [`DeferQueuePromotion`](/docs/core/trains-and-junctions#making-the-side-effect-durable) is the exception: its entry is committed unconfirmed (never dispatched) before its `OnQueue` hook runs, and removed again if the hook throws, so a refused enqueue of such a train does briefly write a row.

Upgrading from a version where this mutation wrote the row itself: it now authorizes, fires `OnQueue` and stamps the subject key. See [Enqueue and Outcome Changes](/docs/migration-guides/enqueue-and-outcome-changes).

The entry is created through [`ITrainExecutionService.QueueAsync`](/docs/sdk-reference/mediator-api/train-execution#queueasync), so the train's `[TraxAuthorize]` requirements apply on top of the operations gate. Authorization runs before `inputJson` is read: a caller who may not run the train gets a GraphQL error with code `TRAX_AUTHORIZATION` and message `"Not authorized."`, not `success: false`, even when the input is malformed, and nothing is inserted. See [Authorization: The Operations Surface](/docs/authorization#the-operations-surface).

Four kinds of exception propagate out of the mutation rather than becoming `success: false`. Authorization (an `UnauthorizedAccessException`, which `TrainAuthorizationException` is) surfaces as the `TRAX_AUTHORIZATION` error above. Cancellation of the request ends it. An infrastructure failure, meaning a database, EF Core, network, I/O or timeout exception anywhere in the exception's chain (a `DbException` such as `NpgsqlException`, `DbUpdateException`, `TimeoutException`, `SocketException`, `HttpRequestException` or `IOException`), is logged on the server and arrives as a GraphQL error with HotChocolate's masked `"Unexpected Execution Error"` message, so nothing about the server reaches the caller. That includes a data-layer exception caused by the train's own `OnQueue` hook; a hook that means to refuse throws its own exception. The mediator's `TrainAuthorizationNotConfiguredException`, for a `[TraxAuthorize]` train on a host with no `ITrainAuthorizationService` registered, is a host misconfiguration, so it is logged and masked the same way.

Every other exception from the enqueue is a refusal and becomes `success: false`: invalid JSON as `"Invalid InputJson: "` followed by the parser's message, an oversized input as the generic `"The train input failed validation."` (neither the cap nor the input's size is echoed), and anything else as `"The enqueue was refused: "` followed by the exception's message. That last group covers the train's `OnQueue` hook throwing, `QueueSubjectKey` throwing or returning an empty key, one that is only whitespace, or one longer than 512 Unicode characters, and a deferred entry being cancelled before it was confirmed (in which case the hook's side-effect may already have landed). `Trax.Scheduler/docs/adr/0004` records the split.

```graphql
mutation {
  operations {
    workQueue {
      queueTrain(input: {
        trainName: "Trax.Samples.GameServer.Trains.Combat.IResolveCombatTrain"
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
| `trainName` | `String!` | Yes | N/A | Train interface FullName (matches the `serviceTypeName` returned by `operations.trains`) |
| `inputJson` | `String` | No | `null` | JSON payload that deserializes to the train's input type. `null` or blank is read as `{}`, so a train whose input needs no values can be queued without one; an input type that needs values (a positional record whose parameters have no defaults, or a `required` member) is refused. The JSON literal `null` is refused |
| `priority` | `Int` | No | `0` | Dispatch priority 0-31. Values outside that range are clamped |
| `scheduledAt` | `DateTime` | No | `null` | Earliest UTC time the entry should be picked up. Null means dispatch immediately |

**Returns**: `OperationResponse`. On success, `count` is `1` and `message` includes the new entry's ID.

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

Cancels many queued entries in one round-trip (a single set-based `UPDATE`). Only entries
still `QUEUED` are affected; already-dispatched or already-cancelled ids in the list are
silently skipped. Backs the dashboard's bulk-cancel selection.

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
| `ids` | `[Long!]!` | Yes | Work queue entry ids to cancel |

**Returns**: `OperationResponse`. `count` is the number actually cancelled.

---

### deadLetters (nested namespace)

The `operations.deadLetters` namespace exposes dead-letter requeue and acknowledge mutations: `requeueDeadLetter`, `acknowledgeDeadLetter`, batch variants (`requeueDeadLetters`, `acknowledgeDeadLetters`), and "all" variants (`requeueAllDeadLetters`, `acknowledgeAllDeadLetters`). See [scheduler/dead-letters-and-cleanup](/docs/scheduler/dead-letters-and-cleanup) for full details and examples.

---

## OperationResponse

Shared response type for operations mutations.

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether the operation succeeded |
| `count` | `Int` | Number of affected records (only populated by `cancelManifest`, `triggerGroup`, `cancelGroup`) |
| `message` | `String` | Human-readable status message |
