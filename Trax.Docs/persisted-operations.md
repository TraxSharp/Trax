---
layout: default
title: Persisted Operations
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

Ids are opaque strings. There is no parse rule — `userProfile_v1`, `userProfile`, or `userProfile-2026-05` are all valid. The convention `<name>_v<N>` is recommended for readability but not enforced.

The `version` field on the row is operator-controlled metadata, set via `UpsertOptions.Version` (or the `version` input on the GraphQL mutation). It is **not used for request routing** — the id is the contract with shipped clients. Bump the version when shipping a new client that requests a new id; both ids coexist in the database for the rollover period.

The convention is built-time stable, not content-derived. Apollo's automatic-persisted-queries (APQ) hash the document text and produce a different id whenever the text changes, defeating the hot-fix property; persisted operations do the opposite.

## Setup

The minimum configuration enforces persisted-only requests, hits the database on every request (no cache), and uses no broadcaster:

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql
    .AddDbContext<ClientDataContext>()
    .UsePersistedOperations(opts => opts
        .UseDatabase(builder.Configuration.GetConnectionString("Trax")!)
        .RequirePersisted(true)
    )
);

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UsePersistedOperationsEnforcement();   // after auth, before GraphQL
app.UseTraxGraphQL();
```

The middleware reads the request body once with buffering enabled so HotChocolate's downstream parser can re-read it.

### With cache (single node)

```csharp
.UsePersistedOperations(opts => opts
    .UseDatabase(connectionString)
    .RequirePersisted(true)
    .WithInMemoryCache()
)
```

The cache is a pure optimization. The default behavior (DB hit per request) is correct for the vast majority of deployments; opt in only when measurements show the lookup is hot.

### With cache + multi-node invalidation

```csharp
.UsePersistedOperations(opts => opts
    .UseDatabase(connectionString)
    .RequirePersisted(true)
    .WithInMemoryCache()
    .UseRabbitMqInvalidation(rabbitConnectionString)
)
```

The RabbitMQ broadcaster publishes a `PersistedOperationChangedMessage` on every upsert, deactivate, and restore. Each node binds an exclusive auto-delete queue to a fanout exchange (`trax.persisted_operations.invalidation`) and clears its local cache entry on receipt.

### HotChocolate cache invalidation

Upsert, deactivate, and restore each clear HotChocolate's request-pipeline caches in addition to Trax's optional `IPersistedOperationCache`:

- `IDocumentCache` (root-scoped, keyed by persisted-operation id) holds the parsed `DocumentNode`.
- `IPreparedOperationCache` (schema-scoped, keyed by `{schema}-{executorVersion}-{documentId}+{operationName}`) holds the compiled operation.

Without this, a re-uploaded document text would be visible from `IPersistedOperationStore.GetAsync` but the request executor would keep serving the previously compiled operation until the process restarted. Cross-node invalidation via `UseRabbitMqInvalidation(...)` triggers the same HotChocolate cache clear on every receiver. Neither HC cache exposes per-id removal, so each invalidation clears the cache entirely; persisted-operation edits are operator-driven and rare, so the cache-warm cost on the next handful of requests is acceptable.

## Phased rollout

A consumer flipping enforcement on for the first time will reject every shipped client that hasn't had its manifest uploaded. Use shadow mode to observe the gap before enforcing:

```csharp
.UsePersistedOperations(opts => opts
    .UseDatabase(connectionString)
    .RequirePersisted(false)            // do not reject
    .LogNonPersistedRequests(true)      // log everything that would be rejected
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
opts.AllowOperationsMatching(id => id.StartsWith("dev_"))
```

Introspection requests (`IntrospectionQuery`, or any query whose top-level selection set is purely `__schema` / `__type`) bypass enforcement automatically. Disable with `DisableIntrospection()` for tight prod.

## Managing operations

There are three surfaces, all backed by the same `IPersistedOperationStore` and the same shape-diff and schema-validation guardrails.

### From the dashboard

When `UsePersistedOperations(...)` is wired into the API host, the Trax dashboard exposes a **Persisted Operations** entry under **Data**. The page lists every row in `trax.persisted_operation`, supports filtering, and offers Upload / Edit / Deactivate / Restore actions. The editor renders parse, schema-validation, and shape-diff errors inline so the operator never has to read a stack trace.

If `UsePersistedOperations(...)` was not called, the sidebar entry is hidden and direct navigation to `/trax/data/persisted-operations` renders a "not enabled on this server" panel. The dashboard probes the runtime via `IServiceProvider.GetService<IPersistedOperationsCapability>()`.

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
| `INVALID_INPUT` | `id` or required fields were empty. |

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

The management mutations and queries always bypass the enforcement middleware (their names are intrinsic to the subsystem; persisting them by id would be a chicken-and-egg). They are protected only by whatever ASP.NET auth middleware sits in front of the GraphQL endpoint.

### Programmatically

`IPersistedOperationStore` is the in-process admin surface, registered automatically when the package is configured. Useful for tests, custom tooling, or one-off scripts:

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

`IPersistedOperationStore.UpsertAsync` throws one of three structured exceptions on failure:

| Exception | When |
|---|---|
| `PersistedOperationParseException` | Syntax error. Carries `Line`, `Column`, `OriginalMessage`. |
| `PersistedOperationValidationException` | Schema mismatch (unknown field, wrong variable type, etc.). Carries `IReadOnlyList<ValidationFailure>`. |
| `ShapeDiffViolationException` | Edit changes the response shape of an existing id. Carries `OldFingerprint`, `NewFingerprint`. |

All three inherit `PersistedOperationException`. The GraphQL mutations and the dashboard editor project them into structured error payloads with the codes documented above.

Hosts using `AddPersistedOperationStore(...)` (admin tooling without a HotChocolate schema in process) get a no-op validator instead. The shape-diff guardrail still runs.

## Shape-diff guardrail

Every `UpsertAsync` computes a canonicalized structural hash (sha-256) of the response shape using the document AST. The fingerprint is stored in the row alongside the document. The dashboard editor (v1.1) will compare old vs new fingerprints and refuse a save that changes the response shape unless the operator passes `--force`.

The fingerprint considers these the same shape: whitespace, field reordering, argument changes, variable additions, type-extension swaps that preserve fields. It treats these as different: adding/removing/renaming a field, alias changes, fragment-spread vs inlined fields, `@include` / `@skip` directive changes, mutation vs query.

## What lives where

| Component | Schema | Purpose |
|---|---|---|
| `trax.persisted_operation` | Postgres `trax` | Live id -> document mapping |
| `trax.persisted_operation_history` | Postgres `trax` | Append-only audit of every change |
| `IPersistedOperationStore` | DI | Programmatic CRUD |
| `IPersistedOperationValidator` | DI | Schema validation at upsert time (HotChocolate-backed when `UsePersistedOperations`, no-op for `AddPersistedOperationStore`) |
| `IPersistedOperationsCapability` | DI | Marker registered by `UsePersistedOperations`; dashboard probes for it to gate the management UI |
| `IOperationDocumentStorage` | HotChocolate hot path | Resolves id to document for the request executor |
| `PersistedOperationsMiddleware` | ASP.NET pipeline | Enforces inline-query rejection / shadow logging / allowlist |
| `IPersistedOperationBroadcaster` | DI | Multi-node cache invalidation (no-op default) |

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [UsePersistedOperationsEnforcement](/docs/sdk-reference/persisted-operations/use-persisted-operations-enforcement) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) | [IPersistedOperationValidator](/docs/sdk-reference/persisted-operations/i-persisted-operation-validator) | [PersistedOperationExceptions](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) | [Management mutations](/docs/sdk-reference/persisted-operations/management-mutations) | [PersistedOperation](/docs/sdk-reference/persisted-operations/persisted-operation) | [PersistedOperationsDbContext](/docs/sdk-reference/persisted-operations/persisted-operations-db-context) | [ShapeFingerprintComputer](/docs/sdk-reference/persisted-operations/shape-fingerprint-computer)
