---
title: "dispatch mutations"
description: "Reference for the dispatch mutation namespace: the typed mutations Trax generates for trains marked TraxMutation, their modes and response type."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 1
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/DispatchMutations.cs
  - Trax.Api/src/Trax.Api.GraphQL/TypeModules/TrainTypeModule.cs
---

# dispatch mutations

The `dispatch` field on the root mutation type holds one field per train marked with [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute), the generated fields of `DispatchMutations`.

Only trains with the attribute appear under `dispatch`; trains marked `[TraxQuery]` appear under [`discover`](/docs/sdk-reference/graphql-api/queries/discover) instead.

Each such train gets a single mutation field named after the train (no prefix). Trains with `Namespace` set are grouped under a sub-namespace (e.g. `dispatch { alerts { createAlert } }`). The mutation's parameters and behavior depend on the operations passed to the attribute constructor:

- **Run + Queue (default)**: when no operations are specified (or both `GraphQLOperation.Run` and `GraphQLOperation.Queue` are passed), the mutation accepts an optional `mode: ExecutionMode` parameter (`RUN` or `QUEUE`, default `RUN`) and an optional `priority: Int`.
- **Run only**: the mutation always runs synchronously. No `mode` or `priority` parameters.
- **Queue only**: the mutation always queues. Has `priority` but no `mode` parameter.

## Field names

The field name is derived from the train's service interface name (or overridden via `[TraxMutation(Name = "...")]`):
1. Strip the `I` prefix
2. Strip the `Train` suffix
3. Use the result as the field name (camelCase)

For example, `IBanPlayerTrain` produces `banPlayer`.

## Example

Given a train annotated with `[TraxMutation]`:

```csharp compile
using Trax.Effect.Models.Manifest;

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

## Response type

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

### Queued runs

A QUEUE mutation returns before any run exists, so it has no `metadataId`. Its `externalId` is the correlation key: the JobDispatcher copies it onto the execution (`trax.metadata.external_id`) it creates for the entry, so the run's lifecycle events, its `operations.executions` row and the work queue entry (`trax.work_queue.external_id`) all carry the same value. If a delivery to the runner fails and the entry is requeued (`MaxDispatchAttempts`), each attempt gets its own execution row with that same external id, and the failed attempts it requeues are recorded `Failed` by the dispatcher without a lifecycle event. The attempt that fails for good (its attempts exhausted, or its stored input unreadable) is the exception: no runner started it, so the dispatcher publishes its failure (`onTrainFailed` and a `FAILED` `onTrainStateChanged`, with `failureException` `DispatchFailed` and a fixed reason; the cause stays on the row). Otherwise only an attempt a runner actually started emits events. [Watch a queued run](/docs/api/watch-a-queued-run) follows one from a client.

A queued run that fails is not retried: retries belong to [manifests](/docs/scheduler/scheduling-options), and a work queue entry with no manifest runs once. An operator requeues a failed queued run with [`requeueExecution`](/docs/sdk-reference/graphql-api/mutations/executions#requeueexecution) or the dashboard's Re-queue button.

The wrapper is named `{TrainName}Response` by default. If the train's output CLR class is also named `{TrainName}Response` (for example `IAddressValidationTrain` returning `AddressValidationResponse`), the wrapper falls back to `{TrainName}MutationResponse` so the schema can build without a name collision. The output type's name is the one HotChocolate gives it, its `[GraphQLName]` when it has one. Trains whose output type follows a different naming convention are unaffected. If the fallback name is itself taken, by the response type of a train named `{TrainName}Mutation`, the host refuses to start naming both trains; set `Name` on one of them.

## Run and queue mode

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

### Example: run and queue with a typed output

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

## Run-only mode

When `GraphQLOperation.Run` is the only operation passed (e.g. `[TraxMutation(GraphQLOperation.Run)]`), the mutation always runs synchronously. No `mode` or `priority` parameters are generated.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `{TrainName}Input!` | Yes | Strongly-typed input matching the train's input record |

The response type still uses the unified format, but `workQueueId` will always be `null`.

## Queue-only mode

When `GraphQLOperation.Queue` is the only operation passed (e.g. `[TraxMutation(GraphQLOperation.Queue)]`), the mutation always queues. No `mode` parameter is generated, but `priority` is available.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `input` | `{TrainName}Input!` | Yes | N/A | Strongly-typed input matching the train's input record |
| `priority` | `Int` | No | `0` | Dispatch priority (0-31, higher runs first) |

The response type still uses the unified format, but `metadataId` and `output` will always be `null`.

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [discover queries](/docs/sdk-reference/graphql-api/queries/discover)
- [TraxQuery and TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
