---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# A cached persisted operation is never older than the last change or its maximum age

Every persisted-operation cache on a node (HotChocolate's parsed-document and prepared-operation
caches, which Trax replaces, the optional lookup cache, and the record of ids the store does not
hold) serves an entry only while two things hold: no change has been applied on the node since
the request that wrote the entry started, and the document in it was read from the database less
than the maximum age ago (`WithCacheMaxAge`, five minutes by default). The broadcast of
[0026](./0026-a-persisted-operation-id-means-one-document-on-every-node.md) stays the way a change
reaches every node at once; the maximum age is the bound when it does not arrive, and a change
whose broadcast the broker did not confirm says so to whoever made it.

## Status

**Accepted.** Amends [0026](./0026-a-persisted-operation-id-means-one-document-on-every-node.md),
which ruled a TTL out as the way a change reaches every node; here it is the backstop beside the
broadcast, not a replacement for it.

## What the decision is

- Each node counts the changes it applies (a generation). A Trax request middleware ahead of
  HotChocolate's document cache records the generation when the request starts; every entry the
  request writes carries it, and an entry from an earlier generation is never returned. A request
  that read a document before a change and finishes after it therefore cannot put the old
  document back, however long it runs.
- Every entry also carries the time its document was read from the database, carried forward
  from the lookup cache and the document cache into the prepared-operation cache, so no layer
  restarts the clock. Past the maximum age the entry is not returned and the store is read again.
  The lookup cache's TTL defaults to the maximum age and may not exceed it.
- A request that sends its own document is not looked up in the store; the document runs as
  sent. An id the store does not hold is remembered, in a bounded cache, until the next change or
  the maximum age.
- The RabbitMQ broadcaster publishes with publisher confirms. A change that was saved but not
  confirmed within ten seconds is reported: the store throws `PersistedOperationNotBroadcastException`
  (`CHANGE_NOT_BROADCAST`) after applying the change to its own node, and the management mutations
  return the saved operation with that error, not success. Repeating the change sends it again.
- `AddPersistedOperationStore` takes the same declaration `UsePersistedOperations` does: a broker
  or `SingleNode()`, and refuses to start with neither.

## Considered options

**Clear again after a request that overlapped a change.** A request that saw the generation move
would empty the caches when it finished. Rejected: the late write is visible until that second
clear, and under steady load each clear overlaps further requests, so the clears chain. Refusing a
stale stamp at read time has no window and no chain; it is the guard Facebook's memcache leases
put on a fill that a delete overtook.

**A per-request version check against the database.** Rejected in 0026 for putting a read on
every request, and still rejected.

**A durable outbox the nodes poll.** It would survive a crash between the commit and the
publish, which a failed publish cannot report. Rejected for now: each node polling the outbox is
a periodic read whose staleness bound is its poll interval, which is what the maximum age already
gives without a table, a poller or a cleanup job.

**Fail the mutation when the broadcast fails.** Rejected: the change is committed and in force on
the node that made it, so an error saying it failed is false, and a masked error
([0028](./0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md)'s rule for a
server failure) would hide that it happened at all. The operator needs both facts, so the payload
carries the saved operation and a coded error, `success: false`. The message is fixed text; the
broker's error goes only to the log.

**Read the store for a request that carries a document.** HotChocolate names such a request by
its document's hash and asks the store for it, for automatic persisted queries. Trax does not
use them, and the id-binding rule of 0026 already guarantees a document runs only under its own
hash, so the read could only add a database query per inline request.

## Consequences

**A node that missed a change serves the old document for at most the maximum age.** A shorter
age costs a database read and a recompile per id per node per period.

**The miss cache bounds repeats, not variety.** The same unknown id asked again costs nothing,
but each distinct id still costs one read. A full in-memory catalogue, as Apollo Router keeps its
persisted-query manifest, would remove that read; it is not built.

## Exemplars

- `PersistedOperationCacheGenerationTests` pins that a deactivation or re-upload made while a
  request is running is what the next request sees.
- `PersistedOperationCacheLifetimeTests` pins the maximum age, that an inline request is refused
  without a store read, and that an unknown id is read once.
- `PersistedOperationBroadcastFailureTests` pins the `CHANGE_NOT_BROADCAST` payload for upload,
  deactivate and restore, and the other node honouring the change within the maximum age.

Not covered: the generation is per node, so a change reaches another node only through the
broadcast or the maximum age; and a crash between the commit and the publish is reported to no one.

## Changelog

- **2026-10-01**: Recorded.
