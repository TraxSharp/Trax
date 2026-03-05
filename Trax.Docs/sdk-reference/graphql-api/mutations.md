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
  dispatch: DispatchMutations!
  operations: OperationsMutations!
}
```

- **`dispatch`** — auto-generated typed mutations for trains annotated with [`[TraxMutation]`]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-graphql-attribute.md %})
- **`operations`** — scheduler management operations (trigger, disable, enable, cancel manifests and groups)

## Dispatch Mutations (Auto-Generated)

Trax auto-generates strongly-typed mutations for trains that opt in with `[TraxMutation]`. Only trains with this attribute appear under `dispatch`. Trains annotated with `[TraxQuery]` appear under `query { discover { ... } }` instead — see [Queries]({{ site.baseurl }}{% link sdk-reference/graphql-api/queries.md %}).

Each whitelisted train gets a `run{TrainName}` mutation, a `queue{TrainName}` mutation, or both, depending on the `Operations` property on the attribute. These provide full GraphQL schema validation on the input, so errors are caught at the schema level rather than at runtime.

### Naming Convention

The mutation names are derived from the train's service interface name (or overridden via `[TraxMutation(Name = "...")]`):
1. Strip the `I` prefix
2. Strip the `Train` suffix
3. Prepend `run` or `queue`

For example, `IBanPlayerTrain` produces `runBanPlayer` and `queueBanPlayer`.

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

mutation {
  dispatch {
    runBanPlayer(input: { playerId: "player-42", reason: "cheating" }) {
      metadataId
    }
  }
}

mutation {
  dispatch {
    queueBanPlayer(
      input: { playerId: "player-42", reason: "cheating" }
      priority: 10
    ) {
      workQueueId
      externalId
    }
  }
}
```

### run{TrainName}

Runs the train synchronously via `ITrainBus`. The call blocks until the train completes.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `{TrainName}Input!` | Yes | Strongly-typed input matching the train's input record |

**Returns**: depends on the train's output type.

#### Trains with `Unit` output (no typed output)

Returns `RunTrainResponse`:

| Field | Type | Description |
|-------|------|-------------|
| `metadataId` | `Long!` | Metadata ID of the completed execution |

#### Trains with typed output

When a train's `ServiceTrain<TIn, TOut>` has a non-`Unit` output type, the mutation returns a per-train response type `Run{TrainName}Response` with the typed output included:

| Field | Type | Description |
|-------|------|-------------|
| `metadataId` | `Long!` | Metadata ID of the completed execution |
| `output` | `{OutputType}!` | The train's typed output, exposed as a GraphQL object type |

For example, a train `ServiceTrain<LookupPlayerInput, LookupPlayerOutput>` annotated with `[TraxMutation]` produces:

```graphql
type RunLookupPlayerResponse {
  metadataId: Long!
  output: LookupPlayerOutput!
}

type LookupPlayerOutput {
  playerId: String!
  rank: Int!
  wins: Int!
  losses: Int!
  rating: Int!
}

mutation {
  dispatch {
    runLookupPlayer(input: { playerId: "player-42" }) {
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
```

The output type is automatically registered as a GraphQL `ObjectType` and deduplicated — if multiple trains share the same output type, only one GraphQL type is generated.

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

**Returns**: `OperationResponse` (includes `count` — number of executions marked for cancellation)

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

**Returns**: `OperationResponse` (includes `count` — number of manifests triggered)

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

**Returns**: `OperationResponse` (includes `count` — number of executions marked for cancellation)

---

## OperationResponse

Shared response type for operations mutations.

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether the operation succeeded |
| `count` | `Int` | Number of affected records (only populated by `cancelManifest`, `triggerGroup`, `cancelGroup`) |
| `message` | `String` | Human-readable status message |
