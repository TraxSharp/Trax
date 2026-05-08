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

### triggerManifest

Triggers an immediate execution of a manifest, bypassing its normal schedule.

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

Triggers immediate execution of all enabled manifests in a group.

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

**Returns**: `OperationResponse` (includes `count`, the number of manifests triggered)

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

### config (nested namespace)

The `operations.config` namespace patches scheduler runtime settings. Writes go to both the in-memory `SchedulerConfiguration` singleton (immediate effect on running services) and the persisted `trax.scheduler_config` row (survives restarts via the `SchedulerConfigBootstrapHostedService`).

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
| `manifestManagerPollingInterval` | `TimeSpan` | |
| `jobDispatcherPollingInterval` | `TimeSpan` | |
| `maxActiveJobs` | `Int` | |
| `clearMaxActiveJobs` | `Boolean` | When `true`, sets `maxActiveJobs` to null |
| `defaultMaxRetries` | `Int` | |
| `defaultRetryDelay` | `TimeSpan` | |
| `retryBackoffMultiplier` | `Float` | |
| `maxRetryDelay` | `TimeSpan` | |
| `defaultJobTimeout` | `TimeSpan` | |
| `stalePendingTimeout` | `TimeSpan` | |
| `recoverStuckJobsOnStartup` | `Boolean` | |
| `deadLetterRetentionPeriod` | `TimeSpan` | |
| `autoPurgeDeadLetters` | `Boolean` | |
| `localWorkerCount` | `Int` | Ignored when `UseLocalWorkers()` is not configured |
| `clearLocalWorkerCount` | `Boolean` | Resets `localWorkerCount` to `Environment.ProcessorCount` |
| `metadataCleanupInterval` | `TimeSpan` | Ignored when metadata cleanup is not configured |
| `metadataCleanupRetention` | `TimeSpan` | Ignored when metadata cleanup is not configured |

**Returns**: `OperationResponse`. `count` is the number of fields actually changed (zero if every supplied value already matched).

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
| `maxActiveJobs` | `Int` | `null` | New per-group concurrency limit. `null` means "no change". To clear the limit, use `clearMaxActiveJobs` instead |
| `clearMaxActiveJobs` | `Boolean` | `false` | When `true`, sets `maxActiveJobs` to `null` (removes the per-group limit). Takes precedence if both this and `maxActiveJobs` are set |
| `priority` | `Int` | `null` | New priority. `null` = no change |
| `isEnabled` | `Boolean` | `null` | Whether the group is active. `null` = no change |

**Returns**: `OperationResponse`. On success, `count` is the number of fields actually changed (zero if every supplied value already matched the persisted row). On failure (group not found), `success` is `false` and `message` explains.

---

### workQueue (nested namespace)

The `operations.workQueue` namespace lets the dashboard (and other API clients) put work into the queue and cancel it. Reads live under [operations.workQueue in queries.md](/docs/sdk-reference/graphql-api/queries#workqueue-nested-under-operations).

#### queueTrain

Creates a new work queue entry. The dispatcher picks it up on its next poll. Validation happens before any DB write: an unknown `trainName`, malformed `inputJson`, or JSON that deserializes to `null` returns `OperationResponse(success: false, message: ...)` and inserts nothing.

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
| `inputJson` | `String` | No | `null` | JSON payload that deserializes to the train's input type. Use `null` for trains with `Unit` input |
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
