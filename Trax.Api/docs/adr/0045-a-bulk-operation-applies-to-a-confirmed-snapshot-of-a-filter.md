---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# A bulk operation applies to a confirmed snapshot of a filter

`startBulkOperation(kind, filter, expectedCount)` applies one action (cancel, requeue, acknowledge,
trigger, enable, disable) to every row a list read's filter matches. It reads the matching ids once,
when it starts, and refuses to start when there are not exactly `expectedCount` of them, the number
the operator was shown and confirmed. There is no cap on how many it matches. It then runs in the
background, as requeue-all does ([0036](./0036-requeue-all-runs-in-the-background-and-returns-a-handle.md)),
in batches through the shared operation the id-list mutation of its kind calls (central `0022`), and
records who started it, how many ids took, and each one that did not with its reason.
`bulkOperation(id)` and `bulkOperations` read it back.

## Status

**Accepted.** Generalises [0036](./0036-requeue-all-runs-in-the-background-and-returns-a-handle.md)
to any filter and kind; `requeueAllDeadLetters` and its job are left as they are.

## Why this is written down

Until 2026-10-18 the batch mutations took id lists of at most 1,000, so "cancel every failing run of
this train since 02:00" meant paging and ticking, and nothing recorded who did it or what failed.
Taking a filter instead removes the size limit, and with it the guard a page gave: the operator no
longer sees every row the action touches. The confirmed count is what replaces that guard, and the
batching is what makes "no cap" safe.

## Considered options

**A cap on the matched count.** Rejected (the user, 2026-10-18): the operator who needs to cancel
300,000 stuck runs needs it most. Instead the operation must pass the stress suite at its scale.

**Apply the filter afresh to each batch.** Rejected: a cancel over "runs in progress" would chase
runs started after the operator confirmed, and the count confirmed would mean nothing. The snapshot
is the set the operator agreed to; a row that leaves the filter before its batch is attempted
anyway and reported by what its row says.

**Durable operation state in a table.** Rejected for the same reasons as in 0036: it needs a
migration in every provider, and nothing depends on it. The batches are idempotent in effect (a
cancelled run stays flagged, a requeued dead letter no longer awaits), so an operation a restart
forgot is started again over what is left. The record that must outlive the node is the audit
entry, written when the host audits the API.

**Build a shared method in `IOperationsService` for it.** Not needed: the filters belong to the API's
list reads (central `0022` lists those reads as API-only), the Blazor dashboard takes no new actions
(central `0055`), and each item still goes through the shared operation, so nothing here validates,
authorizes or writes a row itself.

## Consequences

- Kinds and lists are fixed: `CANCEL` takes runs, `REQUEUE` and `ACKNOWLEDGE` dead letters, and
  `TRIGGER`, `ENABLE`, `DISABLE` manifests. Requeueing runs (not dead letters) in bulk is not
  offered: `requeueExecution` enqueues through the mediator under the caller's per-train
  authorization (central `0017`), which a background batch has no caller for.
- A batch is at most 500 ids, the largest operations page, and is judged by reading its rows back
  once the shared operation ran. A requeue batches by manifest, because the shared requeue folds a
  manifest's dead letters into one queued run only within one call; a manifest with more than 500
  awaiting dead letters gets a batch of up to 1,000.
- One runs per node at a time; a finished one is read for 24 hours, at most 100 are kept, and only
  the first 10,000 failures keep their id (every failure is counted by reason).
- The actor is the caller's principal id, else its sign-in name. The audit entry
  (`operationName: bulkOperation`) carries the counts and never the filter or the note (`0027`).

## Exemplars

- `OperationsBatchMutationsTests` pins the snapshot refusal, the per-id failures and reasons of a
  cancel whose runs end first, the actor and the audit entry, and the requeue batching by manifest.
- `BulkOperationStressTests.cs` measures a cancel over every run in progress and a requeue over every
  dead letter awaiting in the stress seed: the answer, the finish, the longest batch, and list reads
  served meanwhile. It is an explicit stress fixture, so CI does not run it; it is evidence, not a
  guard.

Not covered: nothing checks that an operation started on one node is read on that node, as in 0036.

## Changelog

- **2026-10-18**: Recorded.
