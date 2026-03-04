---
layout: default
title: Mutations
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 3
---

# Mutations

Mutations are split across two type extensions: `TrainMutations` for queuing and running trains, and `SchedulerMutations` for managing scheduled manifests and groups. Additionally, Trax auto-generates **typed mutations** for each registered train.

## Typed Train Mutations (Auto-Generated)

Trax automatically discovers all registered trains and generates a pair of strongly-typed mutations for each one: `run{TrainName}` and `queue{TrainName}`. These provide full GraphQL schema validation on the input, so errors are caught at the schema level rather than at runtime.

### Naming Convention

The mutation names are derived from the train's service interface name:
1. Strip the `I` prefix
2. Strip the `Train` suffix
3. Prepend `run` or `queue`

For example, `IBanPlayerTrain` produces `runBanPlayer` and `queueBanPlayer`.

### Example

Given a train registered as `IBanPlayerTrain` with input type:

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

mutation {
  runBanPlayer(input: { playerId: "player-42", reason: "cheating" }) {
    metadataId
  }
}

mutation {
  queueBanPlayer(
    input: { playerId: "player-42", reason: "cheating" }
    priority: 10
  ) {
    workQueueId
    externalId
  }
}
```

### run{TrainName}

Runs the train synchronously via `ITrainBus`. The call blocks until the train completes.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `{TrainName}Input!` | Yes | Strongly-typed input matching the train's input record |

**Returns**: `RunTrainResponse`

| Field | Type | Description |
|-------|------|-------------|
| `metadataId` | `Long!` | Metadata ID of the completed execution |

### queue{TrainName}

Queues the train for asynchronous execution via the WorkQueue.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `input` | `{TrainName}Input!` | Yes | — | Strongly-typed input matching the train's input record |
| `priority` | `Int` | No | `0` | Dispatch priority (0-31, higher runs first) |

**Returns**: `QueueTrainResponse`

| Field | Type | Description |
|-------|------|-------------|
| `workQueueId` | `Long!` | Database ID of the created WorkQueue entry |
| `externalId` | `String!` | External ID assigned to the WorkQueue entry |

---

## Generic Train Mutations

The generic `queueTrain` and `runTrain` mutations accept any train by name and untyped JSON input. These are available as a fallback when you need to invoke trains dynamically (e.g. from a generic admin UI).

### queueTrain

Queues a train for asynchronous execution via the WorkQueue. The scheduler picks it up and dispatches it on its next polling cycle.

```graphql
mutation {
  queueTrain(
    trainName: "MyApp.Trains.IProcessOrderTrain"
    input: { orderId: 42, customerId: "abc-123" }
    priority: 10
  ) {
    workQueueId
    externalId
  }
}
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `trainName` | `String!` | Yes | — | Fully qualified service type name of the train (use the `trains` query to list available names) |
| `input` | `JSON!` | Yes | — | JSON object matching the train's input type |
| `priority` | `Int` | No | `0` | Dispatch priority (0-31, higher runs first) |

**Returns**: `QueueTrainResponse`

| Field | Type | Description |
|-------|------|-------------|
| `workQueueId` | `Long!` | Database ID of the created WorkQueue entry |
| `externalId` | `String!` | External ID assigned to the WorkQueue entry |

---

### runTrain

Runs a train synchronously via `ITrainBus` on the current machine. The call blocks until the train completes.

```graphql
mutation {
  runTrain(
    trainName: "MyApp.Trains.IProcessOrderTrain"
    input: { orderId: 42, customerId: "abc-123" }
  ) {
    metadataId
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `trainName` | `String!` | Yes | Fully qualified service type name of the train |
| `input` | `JSON!` | Yes | JSON object matching the train's input type |

**Returns**: `RunTrainResponse`

| Field | Type | Description |
|-------|------|-------------|
| `metadataId` | `Long!` | Metadata ID of the completed execution |

---

## Scheduler Mutations

### triggerManifest

Triggers an immediate execution of a manifest, bypassing its normal schedule.

```graphql
mutation {
  triggerManifest(externalId: "order-processing-daily") {
    success
    message
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
  triggerManifestDelayed(
    externalId: "order-processing-daily"
    delay: "00:05:00"
  ) {
    success
    message
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
  disableManifest(externalId: "order-processing-daily") {
    success
    message
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
  enableManifest(externalId: "order-processing-daily") {
    success
    message
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
  cancelManifest(externalId: "order-processing-daily") {
    success
    count
    message
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |

**Returns**: `OperationResponse` (includes `count` — number of executions marked for cancellation)

---

### triggerGroup

Triggers immediate execution of all enabled manifests in a group.

```graphql
mutation {
  triggerGroup(groupId: 1) {
    success
    count
    message
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | The manifest group's database ID |

**Returns**: `OperationResponse` (includes `count` — number of manifests triggered)

---

### cancelGroup

Requests cancellation of all running executions across all manifests in a group.

```graphql
mutation {
  cancelGroup(groupId: 1) {
    success
    count
    message
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | The manifest group's database ID |

**Returns**: `OperationResponse` (includes `count` — number of executions marked for cancellation)

---

## OperationResponse

Shared response type for scheduler mutations.

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether the operation succeeded |
| `count` | `Int` | Number of affected records (only populated by `cancelManifest`, `triggerGroup`, `cancelGroup`) |
| `message` | `String` | Human-readable status message |
