---
title: "operations: bulk operations"
description: "Reference for startBulkOperation, bulkOperations and bulkOperation: one action over every row a list filter matches, run in batches and tracked."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 11
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/OperationsMutations.BulkOperations.cs
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.BulkOperations.cs
  - Trax.Api/src/Trax.Api/DTOs/BulkOperation.cs
  - Trax.Api/src/Trax.Api/DTOs/BulkOperationFilter.cs
---

# operations: bulk operations

A bulk operation applies one action to every run, dead letter or manifest a list read's filter
matches, however many that is. The id-list mutations (`cancelExecutions`, `requeueDeadLetters`,
`acknowledgeDeadLetters`, `triggerManifests`, `setManifestsEnabled`) take at most 1,000 ids and
answer in the request; a bulk operation takes the filter instead, runs in the background on the node
that received it, and records what it did. Both sit under `operations`, behind the host's operations
gate. The design is `Trax.Api/docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md`.

## startBulkOperation

`OperationsMutations.StartBulkOperation`.

```graphql
mutation {
  operations {
    startBulkOperation(
      kind: CANCEL
      filter: { executions: { trainName: "MyApp.IImportTrain", trainState: IN_PROGRESS } }
      expectedCount: 312
    ) {
      success message matched
      operation { id status total }
    }
  }
}
```

| Argument | Type | Description |
|----------|------|-------------|
| `kind` | `BulkOperationKind!` | What to do to each id |
| `filter` | `BulkOperationFilterInput!` | Exactly one list, filtered as its read filters it |
| `expectedCount` | `Int!` | How many matches the operator was shown and confirmed |
| `askAfresh` | `Boolean! = false` | `REQUEUE` and `TRIGGER` only: every new run asks its deciders afresh |
| `note` | `String` | `ACKNOWLEDGE` only, and required there: why, at most 1,000 characters |

The server reads the matching ids once, now, in pages of 10,000. When there are not exactly
`expectedCount` of them it starts nothing: `success` is `false` and `matched` is the number there are
now, to show the operator and confirm again. A list's `totalCount` can be an estimate (unfiltered
runs or manifests) or capped at 10,000 (runs filtered by failure text); the refusal's `matched` is
always exact. Otherwise the operation starts and is returned at once with `status: RUNNING`.

Each `BulkOperationKind` applies to one list, and a filter for another is refused:

| Kind | List (`filter.`) | Each batch calls the shared operation of |
|------|------------------|-------------------------------------------|
| `CANCEL` | `executions` | `cancelExecutions` |
| `REQUEUE` | `deadLetters` | `requeueDeadLetters` |
| `ACKNOWLEDGE` | `deadLetters` | `acknowledgeDeadLetters` |
| `TRIGGER` | `manifests` | `triggerManifests` |
| `ENABLE` / `DISABLE` | `manifests` | `setManifestsEnabled` |

Requeueing runs (rather than dead letters) is not a bulk kind: `requeueExecution` checks the
caller's per-train authorization, and a background batch has no caller.

A batch is at most 500 ids, in its own short transaction, so the console's reads are served between
batches and an operation that stops part-way leaves its finished batches applied. A requeue batches
dead letters by manifest, because the shared requeue folds one manifest's dead letters into a single
queued run only within one call; a manifest with more than 500 awaiting gets a batch of up to 1,000.
After each batch the server reads its rows back: an id is done when it is in the state the
operation asks for (cancellation requested, requeued, acknowledged, triggered, enabled, disabled),
and failed otherwise, with the reason its row gives ("It had already ended (completed).", "It no
longer exists.").

One bulk operation runs per node at a time. While one runs, `startBulkOperation` returns
`success: false` with that one as `operation`.

The caller is recorded as `actor`. When the host audits the API (`AddAudit`), the request that
started the operation is audited as usual, and its outcome is written as a second entry with
`operationName: "bulkOperation"`, the caller's principal, and the operation's id, kind, status and
counts in its metadata. The entry never carries the filter or the note.

### StartBulkOperationResponse

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether an operation was started |
| `message` | `String!` | What happened, or why nothing was started |
| `matched` | `Int` | How many ids the filter matched; `null` when it was not read (a refusal of the arguments) |
| `operation` | `BulkOperation` | The started operation, or the one already running |

### BulkOperationFilterInput

`BulkOperationFilter` as input; give exactly one of `executions`, `deadLetters`, `manifests`. An
omitted field filters nothing, as on the list.

| Type | Fields (as on the list read) |
|------|------------------------------|
| `BulkExecutionFilter` (`executions`) | `trainState`, `trainName`, `startedAfter`, `startedBefore`, `manifestId`, `manifestGroupId`, `hideAdminTrains`, `failureClass`, `externalId`, `parentId`, `hostName`, `failureReasonContains`, `failureJunction` |
| `BulkDeadLetterFilter` (`deadLetters`) | `status`, `manifestId` |
| `BulkManifestFilter` (`manifests`) | `isEnabled`, `scheduleType`, `nameContains`, `manifestGroupId`, `hideAdminTrains` |

## bulkOperations and bulkOperation

`OperationsQueries.GetBulkOperations` and `OperationsQueries.GetBulkOperation`.

```graphql
{
  operations {
    bulkOperation(id: "0f8e2c1a-6d1b-4c3e-9a57-1b2d3c4e5f60") {
      status total done failed message
      failureReasons { reason count }
      failures(take: 100) { id reason }
    }
  }
}
```

`bulkOperations` lists every operation this node knows, newest first; `bulkOperation(id: UUID!)`
reads one, or `null`. A node knows the one running and those that finished in the last 24 hours, at
most 100. The state is in the node's memory, as the requeue-all job's is: another node does not know
the operation, and a restart forgets it. A forgotten operation is started again over what is left,
since the ids it finished no longer match a filter such as "in progress" or "awaiting intervention".

### BulkOperation

| Field | Type | Description |
|-------|------|-------------|
| `id` | `UUID!` | The operation, for `bulkOperation(id)` on the same node |
| `kind` | `BulkOperationKind!` | `CANCEL`, `REQUEUE`, `ACKNOWLEDGE`, `TRIGGER`, `ENABLE` or `DISABLE` |
| `filter` | `BulkOperationFilter!` | The filter it was started with, as given |
| `actor` | `String!` | Who started it: the caller's principal id (`{scheme}:{id}`), else its sign-in name, else `<anonymous>` |
| `askAfresh` | `Boolean!` | Whether new runs ask their deciders afresh |
| `status` | `BulkOperationStatus!` | `RUNNING`; `FINISHED`, every id attempted; `FAILED`, stopped on a refused batch or a server failure (the ids not reached were not attempted); `CANCELED`, the node was shutting down |
| `startedAt` / `finishedAt` | `DateTime!` / `DateTime` | When it started, and when it stopped (`null` while running) |
| `total` | `Int!` | Ids the filter matched when it started |
| `done` | `Int!` | Ids in the state asked for once their batch ran |
| `failed` | `Int!` | Ids their batch ran on that did not take |
| `message` | `String!` | Where it is, or how it ended |
| `failureReasons` | `[BulkOperationFailureReason!]!` | Every failure grouped by `reason`, with its `count`, the most common first |
| `failures(take: Int! = 100, afterId: Long)` | `[BulkOperationFailure!]!` | Failed ids in id order, each with its `reason`; pass the last `id` as `afterId` for the next page. The first 10,000 failures keep their id; past that they are only counted |
