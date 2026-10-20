---
title: "operations.workQueue mutations"
description: "Reference for the operations.workQueue mutation namespace: queueTrain, runTrain, cancelWorkQueueEntry and cancelWorkQueueEntries."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 8
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/WorkQueueMutations.cs
---

# operations.workQueue mutations

`WorkQueueMutations`, the type behind `mutation { operations { workQueue { ... } } }`.

The `operations.workQueue` namespace lets the dashboard (and other API clients) put work into the queue and cancel it. Reads live under [operations.workQueue in queries.md](/docs/sdk-reference/graphql-api/queries#workqueue-nested-under-operations).

## queueTrain

Creates a new work queue entry. The dispatcher picks it up on its next poll. An unknown `trainName`, malformed or oversized `inputJson`, or JSON that deserializes to `null` returns `OperationResponse(success: false, message: ...)` and inserts nothing. For most trains nothing is written until every check has passed. A train that sets [`DeferQueuePromotion`](/docs/mediator/queued-work#accepting-a-mutation-is-two-writes) is the exception: its entry is committed unconfirmed (never dispatched) before its `OnQueue` hook runs, and removed again if the hook throws, so a refused enqueue of such a train does briefly write a row.

Upgrading from a version where this mutation wrote the row itself: it now authorizes, fires `OnQueue` and stamps the subject key. See [Enqueue and Outcome Changes](/docs/migration-guides/enqueue-and-outcome-changes).

The entry is created through [`ITrainExecutionService.QueueAsync`](/docs/sdk-reference/mediator-api/i-train-execution-service#queueasync), so the train's `[TraxAuthorize]` requirements apply on top of the operations gate. Authorization runs before `inputJson` is read: a caller who may not run the train gets a GraphQL error with code `TRAX_AUTHORIZATION` and message `"Not authorized."`, not `success: false`, even when the input is malformed, and nothing is inserted. See [Authorization: The Operations Surface](/docs/security/authorization-rules#the-operations-surface).

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
| `trainName` | `String!` | Yes | N/A | Train interface FullName, the `fullName` that [`operations.trains`](/docs/sdk-reference/graphql-api/queries/operations#trains) returns (not its `serviceTypeName`, which is a display name) |
| `inputJson` | `String` | No | `null` | JSON payload that deserializes to the train's input type. `null` or blank is read as `{}`, so a train whose input needs no values can be queued without one; an input type that needs values (a positional record whose parameters have no defaults, or a `required` member) is refused. The JSON literal `null` is refused |
| `priority` | `Int` | No | `0` | Dispatch priority 0-31. Values outside that range are clamped |
| `scheduledAt` | `DateTime` | No | `null` | Earliest UTC time the entry should be picked up. Null means dispatch immediately |

**Returns**: `OperationResponse`. On success, `count` is `1` and `id` is the new work queue entry's id, which `message` also names.

## runTrain

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

**Returns**: `OperationResponse`. On success, `id` is the execution's metadata id (pass it to `executionDetail`), not a work queue id, and `count` is `1`.

A host that exposes the operations mutations must register an `IJobSubmitter`, or it refuses to start; see [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql).

## cancelWorkQueueEntry

Cancels a queued entry. Only entries with `status: QUEUED` can be cancelled. Already-dispatched or already-cancelled entries return `OperationResponse(success: false, ...)` without modifying the row. So does an entry a step of a user's state-machine draft queued, whatever its status, with the message [`cancelExecution`](/docs/sdk-reference/graphql-api/mutations/executions#cancelexecution) refuses its run with: a user's draft is read-only to operators.

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

## cancelWorkQueueEntries

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

**Returns**: `OperationResponse`. `count` is the number actually cancelled, zero included. An empty list, or more than 1000 ids, returns `success: false` (`"No ids were given."` for an empty one) and cancels nothing. An entry a user's draft queued is skipped, and the message ends with how many were skipped and why.

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations.workQueue queries](/docs/sdk-reference/graphql-api/queries/work-queue)
