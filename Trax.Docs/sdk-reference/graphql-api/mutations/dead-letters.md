---
title: "operations.deadLetters mutations"
description: "Reference for the operations.deadLetters mutation namespace: requeue and acknowledge one, many or all dead letters, and the requeue-all job."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 9
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/DeadLetterMutations.cs
---

# operations.deadLetters mutations

`DeadLetterMutations`, the type behind `mutation { operations { deadLetters { ... } } }`.

The `operations.deadLetters` namespace exposes dead-letter requeue and acknowledge mutations: `requeueDeadLetter`, `acknowledgeDeadLetter`, batch variants (`requeueDeadLetters`, `acknowledgeDeadLetters`), and "all" variants (`requeueAllDeadLetters`, `acknowledgeAllDeadLetters`). The batch variants take 1 to 1000 ids; an empty or longer list returns a count of zero with the reason in `message` and changes nothing. An acknowledgement `note` is at most 1,000 characters (`DeadLetterMutations.MaxNoteLength`); a longer one is refused (`success: false`, or `count: 0` on the batch and "all" variants) and nothing changes. The three requeues take an optional `askAfresh: Boolean!` (default `false`): left false, a requeued run [replays the failed run's decisions](/docs/scheduler/how-retries-work#retries-replay-decisions) when that is sound; true, it asks its deciders afresh.

`requeueAllDeadLetters` does not hold the request for the whole backlog. It starts the requeue as a background job on the node that received it and returns that job at once, as a `DeadLetterRequeueJob`; read it again with the [`requeueAllJob(id)`](/docs/sdk-reference/graphql-api/queries/dead-letters#requeuealljob) query until `status` is no longer `RUNNING`. The job is not tied to the request: it finishes after the client goes away, and HotChocolate's execution timeout does not apply to it. Only the node shutting down stops it, between pages, and the job ends `CANCELED`. The job is the scheduler's [`IDeadLetterRequeueJobs`](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs), which the dashboard's **Requeue All** starts too, so a requeue-all started from either surface is the one the other sees running.

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

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations.deadLetters queries](/docs/sdk-reference/graphql-api/queries/dead-letters)
- [Dead Letters & Cleanup](/docs/scheduler/dead-letters-and-cleanup)
