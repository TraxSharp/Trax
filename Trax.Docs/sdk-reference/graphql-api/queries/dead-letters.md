---
title: "operations.deadLetters queries"
description: "Reference for the operations.deadLetters query namespace: the paged dead-letter list, one dead letter, and the requeue-all job."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 6
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/DeadLetterQueries.cs
---

# operations.deadLetters queries

`DeadLetterQueries`, the type behind `operations { deadLetters { ... } }`. Dead letters are the manifests that failed more times than their `MaxRetries` (see [Dead Letters & Cleanup](/docs/scheduler/dead-letters-and-cleanup)). The requeue and acknowledge mutations are under [`operations.deadLetters`](/docs/sdk-reference/graphql-api/mutations/dead-letters) on the mutation type.

```graphql
query {
  operations {
    deadLetters {
      deadLetters(status: AWAITING_INTERVENTION, take: 10) {
        items { id manifestId manifestName status reason retryCountAtDeadLetter deadLetteredAt }
        totalCount
        nextCursor
      }
      deadLetter(id: 42) { status resolvedAt resolutionNote retryMetadataId }
    }
  }
}
```

## deadLetters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination) |
| `take` | `Int!` | `25` | Number of records to return. See [Page size](/docs/sdk-reference/graphql-api/queries/paged-result#page-size) |
| `status` | `DeadLetterStatus` | `null` | `AWAITING_INTERVENTION`, `RETRIED` or `ACKNOWLEDGED` |
| `afterId` | `Long` | `null` | Keyset cursor. See [Pagination](/docs/sdk-reference/graphql-api/queries/paged-result#pagination) |
| `manifestId` | `Long` | `null` | Only the dead letters of this manifest, served by `ix_dead_letter_manifest_id`. The dashboard's dead-letter page reads a manifest's failed runs with [`executions(manifestId:, trainState: FAILED)`](/docs/sdk-reference/graphql-api/queries/executions#executions) and the latest one's detail with [`executionDetail`](/docs/sdk-reference/graphql-api/queries/executions#executiondetail) |

**Returns**: `PagedResult<DeadLetterSummary>`

## deadLetter

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The dead letter's id |

**Returns**: `DeadLetterSummary`, or `null` when no dead letter has that id.

### DeadLetterSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Dead letter id |
| `manifestId` | `Long!` | The manifest that was dead-lettered |
| `manifestName` | `String!` | The manifest's train name (the interface FullName), not its external id |
| `status` | `DeadLetterStatus!` | `AWAITING_INTERVENTION` until an operator requeues (`RETRIED`) or acknowledges (`ACKNOWLEDGED`) it |
| `deadLetteredAt` | `DateTime!` | When the ManifestManager wrote it |
| `reason` | `String!` | For example `Max retries exceeded: (3) failures > (2) max retries` |
| `retryCountAtDeadLetter` | `Int!` | The counted failures when it was written |
| `resolvedAt` | `DateTime` | When it was requeued or acknowledged |
| `resolutionNote` | `String` | The note an acknowledge took, or the one a requeue wrote: `Re-queued (WorkQueue {id})`, single or batch, or `Re-queued with dead letter {id} (WorkQueue {id})` for a dead letter a batch requeue folded into another's run |
| `retryMetadataId` | `Long` | The run a requeue started, set once that run is dispatched |

## requeueAllJob

The `operations.deadLetters` namespace exposes paginated dead-letter reads (`deadLetters`, `deadLetter`) and `requeueAllJob(id: UUID!)`, which reads a job [`requeueAllDeadLetters`](/docs/sdk-reference/graphql-api/mutations/dead-letters) started. It returns the `DeadLetterRequeueJob`, or `null` when this node does not know the id: the job was started on another node, this node has restarted since, or it finished more than 24 hours ago. Read a job on the node that started it; the backlog itself is `deadLetters(status: AWAITING_INTERVENTION) { totalCount }` on any node.

```graphql
query {
  operations {
    deadLetters {
      requeueAllJob(id: "6f1c2a7e-0d4b-4f53-9a3e-2b8f1c0d9e11") { status count message finishedAt }
    }
  }
}
```

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations.deadLetters mutations](/docs/sdk-reference/graphql-api/mutations/dead-letters)
- [IDeadLetterRequeueJobs](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs)
