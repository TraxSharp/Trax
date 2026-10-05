---
layout: default
title: Persisted Operations
description: "Persisted GraphQL operations in Trax: the id-to-document contract, hot-fixable changes, setup on one node or many, caching, rollout and managing operations."
nav_order: 11
---

# Persisted Operations

Persisted operations decouple a build-time-stable id (e.g. `userProfile_v1`) from the GraphQL document text the server executes for it. The mobile client (or any shipped consumer) sends only the id; the server holds a manifest mapping ids to documents and can rewrite a document without touching the client binary.

The motivating use case is mobile: a buggy query baked into an iOS or Android build is functionally permanent until the next app-store release. With persisted operations, the server can hot-fix any change that does not alter the response shape (filter fixes, sort direction, pagination size, resolver-path swaps, performance rewrites).

## The contract

```
client: { id: "userProfile_v1", variables: { userId: 42 } }
server: looks up "userProfile_v1" -> "query UserProfile($userId: Int!) { ... }"
server: executes the resolved document, returns response
```

The contract becomes `(operationId, variables) -> response shape`. As long as the JSON shape stays compatible with what shipped clients expect, the document text is fair game.

### What is hot-fixable

| Fixable | Example |
|---|---|
| Wrong `where` filter | `{ status: { eq: "active" } }` -> `{ and: [{ status: { eq: "active" } }, { deleted: { eq: false } }] }` |
| Wrong `order` direction | `[{ created: ASC }]` -> `[{ created: DESC }]` |
| Wrong default `first` / pagination | `first: 10` -> `first: 25` |
| Resolver-path swap | `discover { campaigns { ... } }` -> `discover { models { campaigns { ... } } }` |
| Performance rewrite | Replace a custom train with an equivalent model query |
| Adding non-output args / variables | New optional variables (old clients ignore, new clients pass) |

### What requires a client redeploy

| Not fixable | Why it breaks |
|---|---|
| Adding a field the UI needs | Old clients don't know to read it |
| Renaming or removing a field | Client deserializer expects the old name |
| Changing a field's nullability to required | Old clients may send no value or null |
| Changing a variable's type | Server rejects the old type |

The shape-diff guardrail enforces this contract on every edit (see [Shape-Diff Guardrail](#shape-diff-guardrail) below).

## Versioning

Ids are opaque strings. There is no parse rule - `userProfile_v1`, `userProfile`, or `userProfile-2026-05` are all valid. The convention `<name>_v<N>` is recommended for readability but not enforced.

The `version` field on the row is operator-controlled metadata, set via `UpsertOptions.Version` (or the `version` input on the GraphQL mutation). It is **not used for request routing** - the id is the contract with shipped clients. Bump the version when shipping a new client that requests a new id; both ids coexist in the database for the rollover period.

The convention is built-time stable, not content-derived. Apollo's automatic-persisted-queries (APQ) hash the document text and produce a different id whenever the text changes, defeating the hot-fix property; persisted operations do the opposite.

### An id runs only the document the store holds for it

A request names an id, a document, or both. With the id alone, the server runs the stored document, or refuses with `HC0020` when the id is unknown or deactivated. With the document alone, the server treats it as inline and enforcement decides. With both, the server refuses the request with `PERSISTED_OPERATION_ID_MISMATCH` (HTTP 400) unless the id is the document's own hash, under the executor's hash algorithm. That is the same check APQ makes before it trusts a client-supplied hash, so a client sending an id with the document it hashes from keeps working. HotChocolate hashes with MD5 hex by default; for Apollo-style SHA-256 ids, call `AddSha256DocumentHashProvider()` on the schema.

## Setup

The minimum configuration enforces persisted-only requests on a host that runs one node:

```csharp
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<ClientDataContext>()
    .UsePersistedOperations(opts => opts
        .RequirePersisted(true)
        .SingleNode()
    )
);

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
```

Enforcement runs inside HotChocolate's execution pipeline, after the document is parsed and before it is validated, so it needs no ASP.NET middleware and covers every transport the same way: a JSON POST, a GET, a multipart POST, a WebSocket `subscribe`, and a request your own code builds in-process. `UsePersistedOperationsEnforcement()` still compiles for existing hosts and adds nothing; a host that mapped it in a `UseWhen(...)` branch can delete the branch. A JSON-array batch is refused by the endpoint with `HC0009` ("Invalid GraphQL Request") before enforcement runs, so no entry of a batch executes, persisted or not.

Host code that builds a request itself and should run an inline document can say so with HotChocolate's own override:

```csharp
var result = await executor.ExecuteAsync(
    OperationRequestBuilder.New()
        .SetDocument("{ hello }")
        .AllowNonPersistedOperation()
        .Build());
```

No Trax transport sets it, so a remote caller cannot.

### One node or many

Persisted operations refuse to start until you say how a change reaches every node. Each node caches the documents it serves, so an upload, deactivation or restore made on one node reaches another at once only if it is broadcast. Storage reads and writes through the Trax data context `AddEffects(e => e.UsePostgres(...))` registers; `UseDatabase(...)` is obsolete and no longer needed.

| Deployment | Call |
|---|---|
| One process serves the endpoint and writes the store | `SingleNode()` |
| More than one node, or the store is written from another process | `UseRabbitMqInvalidation(rabbitConnectionString)` |

Calling neither, or both, fails at startup with a message naming the fix:

```
Persisted operations need to know how a change reaches every node. Each node caches the documents
it serves, and HotChocolate's caches do not expire, so an upload, deactivation or restore made on
one node is seen by another only if it is broadcast. Call UseRabbitMqInvalidation(connectionString)
when more than one node serves this endpoint, or SingleNode() when exactly one process serves it and
writes the store.
```

An existing single-node host adds `.SingleNode()` to its `UsePersistedOperations(...)` call. The
[Persisted Operations sample](/docs/samples/persisted-operations) is one.

`SingleNode()` is a claim nothing can check at runtime. A second node, or a CI uploader writing the store from its own process, makes it false: a change made there reaches this node only when its cached entry reaches the maximum age.

```csharp
.UsePersistedOperations(opts => opts
    .RequirePersisted(true)
    .UseRabbitMqInvalidation(rabbitConnectionString)
)
```

The RabbitMQ broadcaster publishes a `PersistedOperationChangedMessage` on every upsert, deactivate, and restore, in publisher-confirm mode. Each node binds an exclusive auto-delete queue to a fanout exchange (`trax.persisted_operations.invalidation`) and, on receipt, empties its document, prepared-operation and Trax caches. When a node loses its broker connection it empties every cache, and empties them again when the connection recovers, because a change broadcast in between never reached it. When the broker closes the node's channel alone it does the same and subscribes again on a new channel.

When the broker does not confirm a publish within ten seconds, the change is still saved and in force on the node that made it, and the mutation says so: it returns the saved operation with a `CHANGE_NOT_BROADCAST` error instead of success. Repeat the change to send it again.

An uploader that writes the store from its own process declares the same thing: `AddPersistedOperationStore(store => store.UseRabbitMqInvalidation(rabbitConnectionString))`, or `store.SingleNode()` when no other process caches the operations. It refuses to start with neither.

### Maximum age

Every cache entry is served for at most `WithCacheMaxAge` (five minutes by default), counted from when its document was read from the database, then the store is read again. That is the bound for a change that did not reach a node: a broadcast lost while the connection stayed up, a publish the broker did not confirm, or a write from a process that was not declared. A shorter age costs one database read and recompile per id per node per period.

```csharp
.UsePersistedOperations(opts => opts
    .RequirePersisted(true)
    .UseRabbitMqInvalidation(rabbitConnectionString)
    .WithCacheMaxAge(TimeSpan.FromMinutes(2))
)
```

### With the Trax lookup cache

```csharp
.UsePersistedOperations(opts => opts
    .RequirePersisted(true)
    .SingleNode()
    .WithInMemoryCache()
)
```

There are two cache layers. HotChocolate always caches the parsed document and the compiled operation for each id it serves, so even without this the database is read only the first time a node serves an id, after a change empties those caches, and when an entry reaches its maximum age. `WithInMemoryCache()` adds a second layer under them that caches the store's lookups, with a TTL that defaults to the maximum age and may not exceed it. Turn it on only when measurements show the lookup is hot. It is independent of `UseRabbitMqInvalidation`, which reaches both layers.

Two reads never reach the database at all. A request that sends its own document runs it as sent, so under `RequirePersisted(true)` an inline document is refused without a store lookup. And an id the store does not hold is remembered, in a bounded cache, until the next change or the maximum age, so the same unknown id asked again is answered from memory; an upload of that id is served at once.

### HotChocolate cache invalidation

Upsert, deactivate, and restore each clear HotChocolate's request-pipeline caches in addition to Trax's optional `IPersistedOperationCache`:

- `IDocumentCache` (keyed by persisted-operation id) holds the parsed `DocumentNode`.
- `IPreparedOperationCache` (keyed by `{schema}-{executorVersion}-{documentId}+{operationName}`) holds the compiled operation.

Without this, a re-uploaded document text would be visible from `IPersistedOperationStore.GetAsync` but the request executor would keep serving the previously compiled operation until the process restarted. Cross-node invalidation via `UseRabbitMqInvalidation(...)` triggers the same HotChocolate cache clear on every receiver. A document sent inline is cached only under its own hash, never under an id the request names.

HotChocolate treats a persisted-operation id as immutable, so neither of its caches exposes per-id removal (and from version 16, no way to clear them at all), and neither expires. Enabling persisted operations therefore substitutes two cache implementations that can be emptied, with the same contracts as HotChocolate's own, sized from your `ModifyOptions` (`OperationDocumentCacheSize`, `PreparedOperationCacheSize`). Each invalidation empties them; persisted-operation edits are operator-driven and rare, so the cache-warm cost on the next handful of requests is acceptable. A host that does not use persisted operations keeps HotChocolate's caches untouched, and a persisted-operations host whose caches something else replaces refuses to build its executor.

Each entry also carries the change count (generation) of its node when the request that wrote it started. A change advances the count, and an entry from an earlier count is never served, so a request that was already running when an operation was deactivated or re-uploaded cannot put the old document back when it finishes.

Changes to one id are applied one at a time: each takes a transaction-scoped lock on the id before reading the row, so two uploads of the same id cannot both pass the shape-diff check against the same old row.

## Phased rollout

A consumer flipping enforcement on for the first time will reject every shipped client that hasn't had its manifest uploaded. Use shadow mode to observe the gap before enforcing:

```csharp
.UsePersistedOperations(opts => opts
    .RequirePersisted(false)            // do not reject
    .LogNonPersistedRequests(true)      // log everything that would be rejected
    .SingleNode()
)
```

Run for one full release cycle, confirm zero unexpected non-persisted requests in your logs, then flip `RequirePersisted(true)` on a canary environment, then prod.

## Allowlist and dev carve-outs

Operation names that bypass enforcement (case-sensitive):

```csharp
opts.AllowOperations("playground_smoke_test", "DevExplore")
```

Predicate form for patterns:

```csharp
if (builder.Environment.IsDevelopment())
    opts.AllowOperationsMatching(id => id.StartsWith("dev_"));
```

> **The allowlist is a convenience for trusted networks, not a security control.** It matches the `operationName` the caller puts in the request body (or, for an unnamed request, the `documentId` or `id`), and it never looks at the document. `AllowOperations("playground_smoke_test")` admits any inline document with an operation of that name, whatever the operation selects, and the `dev_` predicate above admits any caller who starts a name with `dev_`. Use it for smoke tests and developer tools on a network you already trust. To admit a fixed document from a client you do not control, persist the document and have the client send its id: that is the control. Per-type `[TraxAuthorize]` still applies to allowlisted requests, as it does to every request, because enforcement shapes which documents run and is not the authorization boundary.

Introspection requests bypass enforcement automatically. A request is introspection when its document parses and every top-level selection is `__schema`, `__type` or `__typename`. Disable with `DisableIntrospection()` for tight prod.

## Managing operations

There are three surfaces, all backed by the same `IPersistedOperationStore` and the same shape-diff and schema-validation guardrails. The dashboard and the GraphQL fields both go through `IPersistedOperationsService`, so they accept and refuse the same things with the same codes.

### From the dashboard

The dashboard refuses to start until its options name a posture (`RequirePolicy`, `RequireRoles` or `AllowAnonymousDashboard()`; see [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)). It also needs the scheduler's services for its other pages, so a GraphQL host that adds it registers `AddScheduler(...)` even with no queued work.

When the host registers `IPersistedOperationsService`, the Trax dashboard exposes a **Persisted Operations** entry under **Data**. `UsePersistedOperations(...)` registers it on a GraphQL host, and `AddPersistedOperationStore(...)` on a host that serves no GraphQL, such as a dashboard running in a process of its own. The page lists `trax.persisted_operation` a page at a time, filters by tenant, status and id prefix, and offers Upload / Edit / Deactivate / Restore actions against the row's own tenant. It goes through the same `IPersistedOperationsService` as the `operations.persistedOperations` fields, so an upload or deactivation the API refuses is refused with the same message. The editor renders parse, schema-validation, and shape-diff errors inline so the operator never has to read a stack trace.

A dashboard in its own process should register the store with the broker's connection string, `AddPersistedOperationStore(databaseConnectionString, rabbitConnectionString)`, so its writes are broadcast like any other uploader's.

If neither registration was made, the sidebar entry is hidden and direct navigation to `/trax/data/persisted-operations`, or to an operation's detail page under it, renders a "not enabled on this server" panel. The dashboard asks the container for `IPersistedOperationsService`; it does not need `IPersistedOperationsCapability`, which only `UsePersistedOperations` registers.

### Via GraphQL mutations

Three mutations and three queries are added to the schema by `UsePersistedOperations(...)`, under the `operations.persistedOperations` namespace (matching the convention for every other Trax management feature, like `operations.manifestGroups` and `operations.deadLetters`):

| Field | Purpose |
|---|---|
| `mutation operations.persistedOperations.uploadPersistedOperation` | Insert or update an operation. Runs schema validation and the shape-diff guardrail. |
| `mutation operations.persistedOperations.deactivatePersistedOperation` | Soft-delete; subsequent requests for the id resolve to null. Reason is required. |
| `mutation operations.persistedOperations.restorePersistedOperation` | Reactivate a deactivated row. |
| `query operations.persistedOperations.persistedOperations` | Paginated list. Filterable by active, tenant, id prefix. |
| `query operations.persistedOperations.persistedOperation` | Single row by id. |
| `query operations.persistedOperations.persistedOperationHistory` | Audit log, most-recent-first. |

Mutations never throw to the client. Failures come back as a payload `errors[]` entry with a stable `code`:

| Code | Meaning |
|---|---|
| `PARSE_FAILED` | Document failed to parse. The error carries `locations { line column }`. |
| `SCHEMA_VALIDATION_FAILED` | Document references a field, type, or variable the server schema does not have. The error carries one entry per failure with `message` and (when available) `locations`. |
| `SHAPE_DIFF_VIOLATION` | The edit would change the response shape of an existing id. The error carries `oldFingerprint` and `newFingerprint`. Pass `bypassShapeDiff: true` when the change is verified safe. |
| `NOT_FOUND` | Deactivate or restore against an unknown id. |
| `INVALID_INPUT` | `id` or required fields were empty, or the document holds more than one operation. A persisted document holds exactly one. |

Example upload:

```graphql
mutation Upload($input: UploadPersistedOperationInput!) {
  operations {
    persistedOperations {
      uploadPersistedOperation(input: $input) {
        success
        operation { id shapeFingerprint isActive }
        errors { code message locations { line column } oldFingerprint newFingerprint }
      }
    }
  }
}
```

```json
{ "input": { "id": "userProfile_v1", "document": "query UserProfile($id: Int!) { user(id: $id) { id name email } }" } }
```

The management mutations and queries always bypass enforcement, because persisting them by id would be a chicken-and-egg. The carve-out is decided from the document's structure, not its text: it applies only when every operation in the request selects `operations` at the root and nothing but `persistedOperations` beneath it. A document that mixes the management surface with any other field, including another `operations` namespace such as `deadLetters`, does not qualify and is enforced normally, as is one that does not parse. An alias or a string argument that happens to read `persistedOperations` is neither the field being selected nor part of the document's structure, so it does not qualify either.

The carve-out is not an authorization boundary. Enforcement is a request-shaping control; what protects the management surface is the operations namespace's authorization posture: `GateOperations(...)`, the builder's `RequireAuthorization()`, or an explicit `AllowAnonymousOperations()`. A host that exposes the namespace with none of the three refuses to start.

### Programmatically

`IPersistedOperationsService` is the in-process management surface, registered by both `UsePersistedOperations` and `AddPersistedOperationStore`. It takes the same inputs and returns the same payloads as the GraphQL fields, and never throws for a refused change:

```csharp
var service = serviceProvider.GetRequiredService<IPersistedOperationsService>();
var result = await service.UploadAsync(
    new UploadPersistedOperationInput("userProfile_v1", "query UserProfile($id: Int!) { user(id: $id) { id name email } }"),
    cancellationToken
);
if (!result.Success)
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.Code}: {error.Message}");
```

`IPersistedOperationStore` is the lower-level store underneath it, which throws the structured exceptions below. Useful for tests or one-off scripts:

```csharp
var store = serviceProvider.GetRequiredService<IPersistedOperationStore>();
await store.UpsertAsync(
    "userProfile_v1",
    "query UserProfile($id: Int!) { user(id: $id) { id name email } }",
    options: null,
    cancellationToken
);
```

Every upsert / deactivate / restore writes a row to `trax.persisted_operation_history` for audit and rollback.

## Validation

Every upsert runs the candidate document through HotChocolate's standard validation rules against the live schema before any row is written. The same rules HotChocolate runs at request time, so anything that passes here will execute at runtime (modulo runtime data shape).

`IPersistedOperationStore.UpsertAsync` throws one of four structured exceptions on failure:

| Exception | When |
|---|---|
| `PersistedOperationParseException` | Syntax error. Carries `Line`, `Column`, `OriginalMessage`. |
| `PersistedOperationValidationException` | Schema mismatch (unknown field, wrong variable type, etc.). Carries `IReadOnlyList<ValidationFailure>`. |
| `ShapeDiffViolationException` | Edit changes the response shape of an existing id. Carries `OldFingerprint`, `NewFingerprint`. |
| `PersistedOperationInputException` | The document holds no operation, or more than one. Code `INVALID_INPUT`. |

All four inherit `PersistedOperationException`. The GraphQL mutations and the dashboard editor project them into structured error payloads with the codes documented above.

Hosts using `AddPersistedOperationStore(...)` (admin tooling without a HotChocolate schema in process) get a no-op validator instead. The shape-diff guardrail still runs. `AddPersistedOperationStore` is complete on its own: the store resolves from a container with nothing else from this package, and with no request executor there is no HotChocolate cache to empty after a write.

### Coexistence with `@authorize`

The HotChocolate-backed validator runs the same rules HotChocolate runs at request time, including the authorize-rule aggregator that fires whenever `services.AddAuthorization()` has been wired into the GraphQL builder (which Trax does on every host). The validator resolves `IAuthorizationHandler` from the host service provider and seeds it into the validation context so the rule passes; no upload-time auth check actually fires (Trax uses `ApplyPolicy.BeforeResolver`, so the authorize directive is invoked at execution time only).

## Shape-diff guardrail

Every `UpsertAsync` computes a canonicalized structural hash (sha-256) of the response shape using the document AST. The fingerprint is stored in the row alongside the document. On an edit of an existing id, the store compares the old and new fingerprints and refuses a change of response shape with `ShapeDiffViolationException` (`SHAPE_DIFF_VIOLATION`) unless the caller sets `UpsertOptions.BypassShapeDiff` (`bypassShapeDiff` on the upload mutation, the **Bypass shape-diff guardrail** checkbox in the dashboard editor). The check runs in the store, so every path that writes goes through it.

The fingerprint considers these the same shape: whitespace, field reordering, argument changes, variable additions, type-extension swaps that preserve fields. It treats these as different: adding/removing/renaming a field, alias changes, fragment-spread vs inlined fields, `@include` / `@skip` directive changes, mutation vs query.

## What lives where

| Component | Schema | Purpose |
|---|---|---|
| `trax.persisted_operation` | Postgres `trax` | Live id -> document mapping |
| `trax.persisted_operation_history` | Postgres `trax` | Append-only audit of every change |
| `IPersistedOperationsService` | DI | Management surface shared by the GraphQL fields and the dashboard |
| `IPersistedOperationStore` | DI | Programmatic CRUD |
| `IPersistedOperationValidator` | DI | Schema validation at upsert time (HotChocolate-backed when `UsePersistedOperations`, no-op for `AddPersistedOperationStore`) |
| `IPersistedOperationsCapability` | DI | Marker registered by `UsePersistedOperations`, meaning this process serves the management GraphQL fields. The dashboard does not read it: its pages appear whenever `IPersistedOperationsService` is registered |
| `IOperationDocumentStorage` | HotChocolate hot path | Resolves id to document for the request executor |
| `PersistedOperationEnforcementMiddleware` | HotChocolate execution pipeline (after parsing, before validation) | Enforces inline-query rejection / shadow logging / allowlist on every transport, HTTP and WebSocket alike |
| `IPersistedOperationBroadcaster` | DI | Multi-node cache invalidation (RabbitMQ with `UseRabbitMqInvalidation`, no-op with `SingleNode()`) |

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [UsePersistedOperationsEnforcement](/docs/sdk-reference/persisted-operations/use-persisted-operations-enforcement) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service) | [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) | [IPersistedOperationValidator](/docs/sdk-reference/persisted-operations/i-persisted-operation-validator) | [PersistedOperationExceptions](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) | [Management mutations](/docs/sdk-reference/persisted-operations/management-mutations) | [PersistedOperation](/docs/sdk-reference/persisted-operations/persisted-operation) | [ShapeFingerprintComputer](/docs/sdk-reference/persisted-operations/shape-fingerprint-computer)
