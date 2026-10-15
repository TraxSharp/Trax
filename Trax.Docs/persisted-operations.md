---
title: Persisted operations
description: "How persisted GraphQL operations work in Trax: the id-to-document contract, what can be hot-fixed, caching across nodes, and what enforcement does not protect."
type: explanation
area: api
nav_order: 4
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations
---

# Persisted operations

A persisted operation separates a stable id, such as `userProfile_v1`, from the GraphQL document
the server runs for it. A shipped client sends only the id and its variables; the server holds the
document and can rewrite it without touching the client. The setup is in
[Turn on persisted operations](/docs/api/turn-on-persisted-operations).

The case it was built for is mobile. A wrong query baked into an iOS or Android build stays wrong
until the next store release, and some users never update. With persisted operations the server
fixes any change that does not alter the response shape the shipped client reads.

## The contract

```
client: { id: "userProfile_v1", variables: { userId: 42 } }
server: looks up "userProfile_v1" -> "query UserProfile($userId: Int!) { ... }"
server: runs the stored document, returns the response
```

The contract becomes `(operationId, variables) -> response shape`. As long as the JSON shape stays
what shipped clients expect, the document text can change.

| Fixable on the server | Example |
|---|---|
| A wrong `where` filter | `{ status: { eq: "active" } }` -> `{ and: [{ status: { eq: "active" } }, { deleted: { eq: false } }] }` |
| A wrong `order` direction | `[{ created: ASC }]` -> `[{ created: DESC }]` |
| A wrong page size | `first: 10` -> `first: 25` |
| A resolver path swap | `discover { campaigns { ... } }` -> `discover { models { campaigns { ... } } }` |
| A performance rewrite | A custom train replaced by an equivalent model query |
| A new optional variable | Old clients leave it out, new clients pass it |

| Needs a client release | Why |
|---|---|
| A field the UI needs | Old clients do not read it |
| A renamed or removed field | The client's deserializer expects the old name |
| A field made required | Old clients may send nothing or null |
| A variable's type changed | The server refuses the old type |

The store holds the line between the two tables itself. Each document is stored with a fingerprint
of its response shape, and an edit to an existing id that changes the fingerprint is refused
unless the operator says the change is safe. Whitespace, field order, argument changes and new
variables keep the fingerprint; adding, removing or renaming a field, an alias, a fragment spread
in place of inline fields, an `@include` or `@skip` change, or a query turned into a mutation
changes it. The algorithm is on
[ShapeFingerprintComputer](/docs/sdk-reference/persisted-operations/shape-fingerprint-computer).
Every upload is also validated against the live schema with the rules HotChocolate runs at request
time, so a stored document is one the server can run.

## Ids are names, not hashes

An id is an opaque string. `userProfile_v1`, `userProfile` and `userProfile-2026-05` are all
valid; `<name>_v<N>` reads well and is not enforced. A row's `version` is metadata for operators and
plays no part in routing: a client that needs a different response shape ships with a new id, and
both ids live in the store for as long as old clients are in use.

This is the opposite of Apollo's automatic persisted queries (APQ), whose id is a hash of the
document text. A hashed id changes whenever the text does, which is exactly the property that
defeats a server-side fix.

A request may name an id, carry a document, or both. With the id alone, the server runs the stored
document, or refuses with `HC0020` when the id is unknown or deactivated. With a document alone,
the document is inline and enforcement decides. With both, the request is refused with
`PERSISTED_OPERATION_ID_MISMATCH` (HTTP 400) unless the id is the document's own hash under the
executor's hash algorithm, the same check APQ makes before it trusts a client-supplied hash. So an
id always means the one document the store holds for it, and a caller cannot run its own document
under a stored id's name.

## Where enforcement runs

Enforcement is a step in HotChocolate's execution pipeline, after the document is parsed and
before it is validated, not ASP.NET middleware. Every transport reaches the executor through that
pipeline, so a JSON POST, a GET, a multipart POST, a WebSocket `subscribe` and a request the host
builds in process all get the same decision. A refused inline document gets
`PERSISTED_OPERATION_REQUIRED` with HTTP 400. A JSON-array batch is refused by the endpoint with
`HC0009` before enforcement runs, so no entry of a batch executes.

Host code that builds a request itself can exempt it with HotChocolate's own
`AllowNonPersistedOperation()` on the request builder. No Trax transport sets it, so a remote caller
cannot.

## One node or many

Persisted operations refuse to start until the host says how a change reaches every node. The
reason is the caches. HotChocolate caches the parsed document and the compiled operation for each
id it serves, and it treats an id as immutable: neither cache can drop one entry, and neither
expires. Trax substitutes caches that can be emptied, with the same contracts and sized from the
host's `ModifyOptions`, and empties them on every upload, deactivation and restore. That reaches the
node that made the change. Another node hears of it only if it is broadcast.

`UseRabbitMqInvalidation` broadcasts each change as a `PersistedOperationChangedMessage` over the
RabbitMQ fanout exchange `trax.persisted_operations.invalidation`, in publisher-confirm mode, and
every node empties its caches when it hears one. A node that loses its broker connection empties every cache, and again when the connection recovers, because a change
broadcast in between never reached it. A publish the broker does not confirm within ten seconds
leaves the change saved and in force on the node that made it, and the mutation reports
`CHANGE_NOT_BROADCAST` rather than success; repeating the change sends it again.

`SingleNode()` claims that one process serves the endpoint and writes the store. Nothing can check
that claim at runtime. A second node, or a CI uploader writing the store from its own process,
makes it false. An uploader or a dashboard that writes the store from another process makes the
same declaration through `AddPersistedOperationStore`, for the same reason.

Behind both sits a maximum age, five minutes by default. A cached entry is served for at most that
long after its document was read from the database, then the store is read again. It bounds the
damage of a change that did not reach a node: a broadcast lost while the connection stayed up, a
publish the broker did not confirm, a write from a process that declared nothing. A shorter age
costs one database read and recompile per id per node per period.

Two more rules keep a stale document from coming back. Each cache entry carries the node's change
count from when the request that wrote it started, and an entry from an earlier count is never
served, so a request already running when an operation was deactivated cannot put the old
document back when it finishes. And changes to one id are applied one at a time, under a
transaction-scoped lock on the id, so two uploads cannot both pass the shape check against the same
old row.

### The optional lookup cache

HotChocolate's two caches mean the database is read only when a node first serves an id, after a
change empties them, and when an entry reaches its maximum age. `WithInMemoryCache()` adds a cache of
the store's lookups under them, with a TTL no longer than the maximum age. It pays off only when
the lookup itself is measured to be hot. Two reads never reach the database in any case: an inline
document under `RequirePersisted(true)` is refused without a lookup, and an unknown id is
remembered in a bounded cache until the next change or the maximum age.

## Allowlist and dev carve-outs

The allowlist (`AllowOperations`, `AllowOperationsMatching`) lets named operations run inline. It
matches the `operationName` the caller puts in the request, or for an unnamed request its
`documentId` or `id`, and it never looks at the document. `AllowOperations("playground_smoke_test")`
admits any inline document with an operation of that name, whatever it selects, and a
`dev_` prefix predicate admits any caller who starts a name with `dev_`.

So the allowlist is a convenience for trusted networks, not a security control. To admit a fixed
document from a client you do not control, persist the document and have the client send its id:
that is the control. Per-type `[TraxAuthorize]` still applies to allowlisted requests, as to every
request, because enforcement shapes which documents run and is not the authorization boundary.

Introspection is let through by default. A request counts as introspection when its document
parses and every top-level selection is `__schema`, `__type` or `__typename`.
`DisableIntrospection()` refuses it too.

## The management surface

Operations are uploaded, deactivated and restored through the `persistedOperations` GraphQL
fields at the root, the dashboard, or `IPersistedOperationsService` in process. All three go through
the same service and the store's shape and schema checks, so they accept and refuse the same
things with the same codes. Every change writes a row to `trax.persisted_operation_history`.

The management fields bypass enforcement, since persisting them by id first would be circular. The
bypass is decided from the document's structure, not its text: it applies only when every
operation selects nothing at the root but `persistedOperations`. A document that mixes in any
other field, `operations` included, is enforced normally, as is one that does not parse; an alias
or a string argument that reads `persistedOperations` does not count.
The bypass holds only while the management fields are served, so a host without `GateManagement`
gets none. Once they are served, the name is Trax's at the root, and a host field called
`persistedOperations` fails startup.

That bypass is not an authorization boundary either. What protects the management fields is their
own gate, which the host declares with `GateManagement(policy, roles)`:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(po => po
        .SingleNode()
        .GateManagement(roles: "Deployer")
    )
);
```

Without it the fields are not in the schema, the dashboard shows no management pages, and
documents are managed out of band. The dashboard's pages check the same gate for the operator,
after the dashboard's own posture. The gate is
separate from the `operations` gate, so a deploy pipeline that uploads documents needs none of the
operator's credential, and turning on persisted operations exposes nothing under `operations`.
Where Trax checks authorization is in
[Where Trax checks authorization](/docs/security/where-authorization-is-checked).

A host without HotChocolate in process, such as a CI uploader or a dashboard running alone, uses
`AddPersistedOperationStore`. A dashboard running alone declares its gate there, with
`store.GateManagement(...)`. A process that also calls `UsePersistedOperations` declares the gate
there only: its options are the ones in force whichever call comes first, and a store gate that
differs from it refuses the host at startup. With no schema to validate against it gets a validator that accepts
every document; the shape check still runs.

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service) | [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) | [Management mutations](/docs/sdk-reference/persisted-operations/management-mutations) | [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) | [ShapeFingerprintComputer](/docs/sdk-reference/persisted-operations/shape-fingerprint-computer)
