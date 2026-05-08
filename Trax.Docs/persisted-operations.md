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

Ids follow `<name>_v<N>` (e.g. `userProfile_v1`). The version is bumped manually when a breaking shape change is required: ship `userProfile_v2` alongside `userProfile_v1` and migrate clients over time.

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

## Programmatic management

`IPersistedOperationStore` is the admin surface, registered automatically when the package is configured. Use it from CI manifest uploaders, custom dashboards, or tests:

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

## Shape-diff guardrail

Every `UpsertAsync` computes a canonicalized structural hash (sha-256) of the response shape using the document AST. The fingerprint is stored in the row alongside the document. The dashboard editor (v1.1) will compare old vs new fingerprints and refuse a save that changes the response shape unless the operator passes `--force`.

The fingerprint considers these the same shape: whitespace, field reordering, argument changes, variable additions, type-extension swaps that preserve fields. It treats these as different: adding/removing/renaming a field, alias changes, fragment-spread vs inlined fields, `@include` / `@skip` directive changes, mutation vs query.

## What lives where

| Component | Schema | Purpose |
|---|---|---|
| `trax.persisted_operation` | Postgres `trax` | Live id -> document mapping |
| `trax.persisted_operation_history` | Postgres `trax` | Append-only audit of every change |
| `IPersistedOperationStore` | DI | Programmatic CRUD |
| `IOperationDocumentStorage` | HotChocolate hot path | Resolves id to document for the request executor |
| `PersistedOperationsMiddleware` | ASP.NET pipeline | Enforces inline-query rejection / shadow logging / allowlist |
| `IPersistedOperationBroadcaster` | DI | Multi-node cache invalidation (no-op default) |

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [UsePersistedOperationsEnforcement](/docs/sdk-reference/persisted-operations/use-persisted-operations-enforcement) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) | [PersistedOperation](/docs/sdk-reference/persisted-operations/persisted-operation) | [PersistedOperationsDbContext](/docs/sdk-reference/persisted-operations/persisted-operations-db-context) | [ShapeFingerprintComputer](/docs/sdk-reference/persisted-operations/shape-fingerprint-computer)
