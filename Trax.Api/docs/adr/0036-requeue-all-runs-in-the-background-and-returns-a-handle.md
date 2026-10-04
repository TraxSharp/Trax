---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# Requeue-all runs in the background and returns a handle

`requeueAllDeadLetters` starts the fold over every dead letter awaiting intervention as a
background job on the node that received it, and answers at once with that job: an `id`, a
`status` (`RUNNING`, `SUCCEEDED`, `FAILED`, `CANCELED`), the number awaiting when it started,
and, once it succeeds, the count and message the fold returned. `requeueAllJob(id)` reads it
again. The fold runs under the host's shutdown token, never the request's, so neither
HotChocolate's execution timeout nor a client that goes away stops it part-way.

## Status

**Accepted.**

## Why this is written down

The fold over a large backlog takes longer than a request may: about 36 s at 500,000 dead
letters, and HotChocolate 16 cancels a request after its default `ExecutionTimeout` of 30 s.
The fold commits a page of manifests at a time, so a cancelled request left most of the work
done and told the client it had failed. The Blazor dashboard calls the scheduler directly and
has no such limit, so the two surfaces answered the same action differently.

## Considered options

**Raise the execution timeout for this field.** HotChocolate's timeout is per request, not per
field, so raising it raises it for every operation on the schema, and a larger backlog still
outgrows any number chosen. It also leaves the request holding a connection and a thread for
the whole fold.

**A background job with a handle.** Chosen. It is the long-running-operation shape the
Microsoft REST API guidelines describe (an operation id, a status of `NotStarted`, `Running`,
`Succeeded`, `Failed` or `Canceled`, and a status resource kept for at least 24 hours), carried
into GraphQL the way bulk APIs such as Shopify's `bulkOperationRunQuery` carry it: the mutation
returns the job object and a query reads it again. HotChocolate has no built-in for this.

**Durable job state, shared by every node.** Rejected for now: it needs a table in
`Trax.Effect`'s migrations and a release of it, and the fold does not need it to be safe. The
job lives in the starting node's memory, so another node answers `requeueAllJob` with `null`
and a restart forgets it. Both are recoverable because the fold is resumable: a requeued dead
letter no longer awaits intervention, so starting another requeue-all requeues exactly the rest,
and two folds on two nodes are safe together (the scheduler retries a page that loses the race
for a manifest's queued entry). The backlog itself, `deadLetters(status:
AWAITING_INTERVENTION).totalCount`, can be read on any node.

## Consequences

- One fold runs per node at a time. Asking again while one runs returns the running one with
  `started: false`, so a retried request does not start a second.
- A finished job can be read for 24 hours, and a node keeps at most 100. After that
  `requeueAllJob` returns `null`.
- A failed job's message says only that the server failed. The detail is logged, as a masked
  error's would be (`0028`).
- The node shutting down stops the fold between pages and the job ends `CANCELED`.

## Exemplars

- `RequeueAllDeadLettersJobTests` pins that the mutation returns a running job, that the fold
  gets the host's stopping token rather than the request's and runs on after the request is
  cancelled, the success, failure and shutdown outcomes, one fold per node, the unknown id, and
  the retention.
- `DeadLetterBatchStressTests` measures the mutation's answer and the whole fold over the
  stress seed, through the request executor.

Not covered: nothing checks that a job started on one node is read on that node; a client
behind a load balancer that spreads requests sees `null` from the others.

## Changelog

- **2026-10-01**: Recorded.
