---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# A persisted-operation id means one document, on every node

A persisted-operation id names the document the store holds for it, and every node serving the
endpoint runs that document and nothing else. Two rules follow. A request that carries both an id
and a document is refused unless the id is the document's own hash, so only the store binds a
document to an id. And persisted operations refuse to start until the host says how a change
reaches every node: `UseRabbitMqInvalidation(...)` when more than one node serves the endpoint,
`SingleNode()` when exactly one process serves it and writes the store.

## Status

**Accepted.**

## Context

Each node caches what it serves in two layers. HotChocolate's parsed-document and
prepared-operation caches are keyed on the request's document id, are always on, and never expire;
Trax substitutes clearable versions of both, which since
[0034](./0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md)
also have a maximum age. The optional Trax lookup cache
(`WithInMemoryCache()`) sits under them with a TTL. An upload, deactivation or restore empties the
caches on the node that made it and broadcasts the change, and the receiver on every other node
empties its own.

## Considered options

**A request carrying an id and a document.** HotChocolate's `AllowDocumentBody` only matters with
its own `OnlyAllowPersistedDocuments` enforcement, which Trax does not use, so it decided nothing
here. Ignoring the body when an id is present was rejected: it silently runs something other than
what the client sent. Refusing unless the id is the body's hash is the check automatic persisted
queries (Apollo's and HotChocolate's) make before trusting a client-supplied hash, so a client
that sends an id with its own document under the executor's hash algorithm keeps working. Leaving it
to enforcement was rejected: the binding has to hold in shadow mode too, where enforcement is off
by design. The document cache also stores an inline document only under its own hash, so the rule
holds for a request built in process as well.

**Reaching every node.** A TTL or a per-request version check on HotChocolate's caches was
rejected: a TTL leaves a window in which a deactivated operation still runs, and a version check
puts a database read back on every request, which is what the caches exist to avoid. Warning on
multi-node use without a broadcaster was rejected because a node cannot see how many others there
are. So the host states it, and the default is to refuse: an existing single-node host adds one
line. The broadcast is no longer tied to `WithInMemoryCache()`, because the caches it must reach
exist without it. A receiver that loses its broker connection empties its caches, and empties them
again when the connection recovers, the standard remedy for a pub/sub invalidation channel that
may have dropped messages (Redis's client-side caching reference prescribes the same).

## Consequences

**`SingleNode()` is a claim nothing can check at runtime.** A second node, or a CI uploader
writing the store from another process, makes it false. `AddPersistedOperationStore` takes the
same declaration, `UseRabbitMqInvalidation(...)` or `SingleNode()`, and refuses to start with
neither, so such an uploader broadcasts its changes.

## Exemplars

- `PersistedOperationIdBindingTests` pins the binding rule over HTTP: an id the store does not
  hold, or has deactivated, does not run a document another caller sent with it; a mismatched
  id and document are refused with `PERSISTED_OPERATION_ID_MISMATCH`; an id that is the
  document's own hash runs it.
- `PersistedOperationCrossNodeTests` runs two nodes over one database and one broker: a
  deactivation or a re-upload on one is seen on the other, and a node that may have missed a
  broadcast empties both HotChocolate caches.
- `BuilderValidationTests` pins the refusal to start with neither a broadcaster nor
  `SingleNode()`, and with both.

Not covered: whether `SingleNode()` is true of the deployment. A broadcast lost while the broker
connection stays up is bounded by the caches' maximum age
([0034](./0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md)),
not prevented.

## Changelog

- **2026-10-01**: The rejected TTL returns as a backstop beside the broadcast, not instead of it
  (0034); `AddPersistedOperationStore` now takes the same broker-or-`SingleNode()` declaration.
- **2026-09-30**: Recorded.
