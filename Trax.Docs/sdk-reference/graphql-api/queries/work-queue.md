---
title: "operations.workQueue queries"
description: "Reference for the operations.workQueue query namespace: the paged work queue and one entry's detail with its input."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 11
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/WorkQueueQueries.cs
---

# operations.workQueue queries

`WorkQueueQueries`, the type behind `operations { workQueue { ... } }`.

The `operations.workQueue` namespace exposes paginated reads of the work queue. The work queue is the intermediary between scheduling and dispatch: every queued execution (manifest triggers, dashboard re-runs, dead-letter requeues, GraphQL `queueTrain` calls) lands here as a `Queued` row that the JobDispatcher picks up.

```graphql
query {
  operations {
    workQueue {
      workQueues(skip: 0, take: 25, status: QUEUED) {
        items {
          id
          externalId
          trainName
          status
          createdAt
          dispatchedAt
          scheduledAt
          priority
          dispatchAttempts
          manifestId
          metadataId
          deadLetterId
          inputTypeName
        }
        totalCount
        isEstimatedCount
        nextCursor
      }
    }
  }
}
```

## workQueues

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0`. Ignored when `afterId` is provided. |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](/docs/sdk-reference/graphql-api/queries/paged-result#page-size) |
| `status` | `WorkQueueStatus` | `null` | Filter by lifecycle state (`QUEUED`, `DISPATCHED`, `CANCELLED`) |
| `trainName` | `String` | `null` | Exact-match filter on the interface FullName (e.g. `MyApp.Trains.Billing.IChargeCustomerTrain`) |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. See [Pagination](/docs/sdk-reference/graphql-api/queries/paged-result#pagination) |
| `subjectKey` | `String` | `null` | Only entries serialized against this subject, matched exactly: a subject's queue, as the dashboard's subject column links to it |
| `manifestId` | `Long` | `null` | Only entries queued for this manifest, served by `ix_work_queue_manifest_id` |

**Returns**: `PagedResult<WorkQueueSummary>`

When any filter or `afterId` is supplied, the count is exact and `isEstimatedCount` is `false`. Unfiltered first-page reads use the same fast estimator as the other large-table queries.

### WorkQueueSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Database ID |
| `externalId` | `String!` | GUID assigned at creation |
| `trainName` | `String!` | Train interface FullName |
| `status` | `WorkQueueStatus!` | `QUEUED`, `DISPATCHED`, or `CANCELLED` |
| `createdAt` | `DateTime!` | When the entry was queued |
| `dispatchedAt` | `DateTime` | When the dispatcher picked it up (null while queued or if cancelled before dispatch) |
| `scheduledAt` | `DateTime` | Earliest dispatch time. Null means dispatch immediately |
| `priority` | `Int!` | Dispatch priority 0-31 |
| `dispatchAttempts` | `Int!` | Number of times dispatch was attempted and failed |
| `manifestId` | `Long` | Source manifest ID, if scheduled |
| `metadataId` | `Long` | Metadata ID created at dispatch, if dispatched |
| `deadLetterId` | `Long` | Dead letter that triggered this requeue, if applicable |
| `inputTypeName` | `String` | Fully qualified type name of the input, for deserialization |
| `confirmedAt` | `DateTime` | When the entry became eligible for dispatch. Null while it is still being staged, and stays null on a staged entry that was cancelled (by an operator, or by the stale staged entry sweep); the dispatcher never claims an unconfirmed entry. Also null on an entry dispatched more than a day before the database was migrated to the version that added it, which the migration does not backfill |
| `subjectKey` | `String` | The subject the entry is serialized against, from the train's [`QueueSubjectKey`](/docs/mediator/queued-work#two-mutations-for-one-record). Null when the train does not set one. The value is computed by the consumer's train, so it may carry record identifiers |
| `replayDecisionsOf` | `Long` | The execution whose recorded decisions the run this entry starts will replay (a requeue, or a manifest's retry that replays); null when it will ask its questions afresh. Also on `detail` |

## detail

Returns one entry with the train input it was queued with and, for a queued entry with a
subject, what it is waiting on. Backs a work queue detail page. It reads through
[`IOperationsService.GetWorkQueueEntryDetailAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#read-models),
the call the dashboard's work queue entry page makes, so both mask the input and name what the
entry waits on the same way.

```graphql
query {
  operations {
    workQueue {
      detail(id: 42) { id status subjectKey input subjectHeldBy subjectQueuedBehind }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The work queue entry's database ID |

**Returns**: `WorkQueueDetail` (nullable, returns `null` if the ID does not exist). It carries every
[`WorkQueueSummary`](#workqueuesummary-fields) field and:

| Field | Type | Description |
|-------|------|-------------|
| `input` | `String` | The train input as stored JSON, with each `[TraxSensitive]` member masked. Can hold credentials; see [Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate) |
| `subjectHeldBy` | `Long` | For a queued entry with a subject: the dispatched entry for the same subject whose run is still pending or in progress. Dispatch skips the subject until that run finishes |
| `subjectQueuedBehind` | `Long` | For a queued entry with a subject that nothing holds: the queued entry for the same subject that dispatch offers first (confirmed, due, in an enabled group, then higher priority, then older). Dispatch offers one entry per subject each cycle |

Both are null for an entry that is not queued or has no subject.

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations.workQueue mutations](/docs/sdk-reference/graphql-api/mutations/work-queue)
