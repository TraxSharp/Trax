---
layout: default
title: Persisted Operations
description: "The PersistedOperations sample: a GraphQL API that runs only stored documents, gated management mutations, a hot-fix by id and the shape-diff guardrail."
parent: Samples & Deployment
nav_order: 6
---

# Persisted Operations

`samples/PersistedOperations` is a GraphQL API that accepts only persisted operations: a client sends
an operation id and its variables, and the server runs the document it stores for that id. A
console client uploads a manifest, calls by id, hot-fixes a stored document without shipping a new
client, and shows the guardrail refusing an edit that would break shipped clients.

## What it proves

| Feature | Where |
|---|---|
| `RequirePersisted(true)`: every inline document refused with `PERSISTED_OPERATION_REQUIRED` | `Program.cs`, `EnforcementTests` |
| `SingleNode()`: the declaration persisted operations refuse to start without | `Program.cs` |
| The management mutations under `operations.persistedOperations`, gated to one role with `GateOperations` | `ProductionPostureTests`, `MutationFlowTests` |
| A shape-preserving edit served on the next request by the same id; a shape-changing edit refused | the Client, `HotFixFlowTests` |
| Upload validation against a schema that carries `@authorize` | `AuthorizedSchemaUpsertTests` |
| The dev allowlist, the demo key and the dashboard only in Development | `Program.cs`, `ProductionPostureTests` |

## Layout

```
samples/PersistedOperations/
├── Trax.Samples.PersistedOperations/          GreetTrain, LookupUserTrain, the gated UserNote model
├── Trax.Samples.PersistedOperations.Api/      the host (Program.cs)
└── Trax.Samples.PersistedOperations.Client/   console client: manifest upload, call by id, hot-fix
```

## Run

From the `Trax.Samples` root:

```bash
docker compose up -d                                                         # Postgres on localhost:5432, database trax_persisted_operations
dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api     # Development, http://localhost:5240

# In a second terminal
dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Client
```

The client prints:

```text
Uploaded greet_v1
Uploaded lookupUser_v1

--- greet_v1 (Alice) ---
{"data":{"discover":{"greeting":{"greet":{"greeting":"Hello, Alice.","greetedAt":"2026-10-03T16:28:08.55Z"}}}}}

--- lookupUser_v1 (user-42) ---
{"data":{"discover":{"users":{"lookupUser":{"userId":"user-42","displayName":"User user-42","email":"user-42@example.test","loginCount":500}}}}}

Hot-fixed greet_v1 (no client redeploy needed).

--- greet_v1 after hot-fix (Alice) ---
{"data":{"discover":{"greeting":{"greet":{"greetedAt":"2026-10-03T16:28:08.55Z","greeting":"Hello, Alice."}}}}}
Hot-fix verified: the server ran the new document.

Shape-changing edit refused: {"code":"SHAPE_DIFF_VIOLATION","message":"Persisted operation 'greet_v1' edit rejected: response shape changed (old fingerprint 86be9cea…, new 2fc9b38b…). ..."}
```

The hot-fix swaps the order of the two fields: the same fields of the same types, so the shape
fingerprint does not change and the edit needs no bypass, yet the response visibly comes from the
new document. The client can run any number of times: its first step re-uploads the manifest,
which restores the original order under the same fingerprint.

## Try it

```bash
G=http://localhost:5240/trax/graphql/

# An inline document is refused
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { greeting { greet(input: {name: \"Eve\"}) { greeting } } } }"}'
# {"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]}

# The same operation by id (after the client has uploaded the manifest)
curl -s $G -H 'Content-Type: application/json' \
  -d '{"id":"greet_v1","variables":{"input":{"name":"Eve"}}}'
# {"data":{"discover":{"greeting":{"greet":{"greetedAt":"...","greeting":"Hello, Eve."}}}}}

# An upload without the operator key is refused
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"mutation Upload($input: UploadPersistedOperationInput!) { operations { persistedOperations { uploadPersistedOperation(input: $input) { success } } } }","variables":{"input":{"id":"x_v1","document":"{ __typename }"}}}'
# {"errors":[{"message":"Not authorized.","path":["operations"],"extensions":{"code":"TRAX_AUTHORIZATION"}}],"data":{"operations":null}}
```

The dashboard is at http://localhost:5240/trax, with the stored operations under
**Data > Persisted Operations**.

## How it works

```csharp
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TraxDatabase")!;
var isDevelopment = builder.Environment.IsDevelopment();

if (isDevelopment)
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add("operator-key-do-not-use-in-production", id: "operator", "Operator"));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(GreetTrain).Assembly)
        .AddScheduler(scheduler => scheduler));   // the dashboard's pages need the scheduler services

builder.Services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts =>
    {
        opts.RequirePersisted(true)
            .LogNonPersistedRequests(true)
            .SingleNode();
        if (isDevelopment)
            opts.AllowOperationsMatching(id => id.StartsWith("dev_"));
    })
    .GateOperations(roles: "Operator"));

if (isDevelopment)
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

var app = builder.Build();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
if (isDevelopment)
    app.UseTraxDashboard();
app.Run();
```

Packages: `Trax.Api`, `Trax.Api.GraphQL`, `Trax.Api.GraphQL.PersistedOperations`,
`Trax.Api.Auth.ApiKey`, `Trax.Effect.Data.Postgres`, `Trax.Mediator`, `Trax.Scheduler`,
`Trax.Dashboard`. A host that calls `UseTraxDashboard()` also sets
`<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` in its csproj.

What each piece is for:

- **`SingleNode()`.** Each node caches the documents it serves, and an upload made on one node
  reaches another only if it is broadcast. Without `SingleNode()` or `UseRabbitMqInvalidation(...)`
  the host refuses to start with "Persisted operations need to know how a change reaches every
  node." This sample is one process that serves the endpoint and writes the store, so `SingleNode()`
  is true of it.
- **No enforcement middleware.** Enforcement runs inside HotChocolate's execution pipeline, which
  `UsePersistedOperations` sets up, so it covers HTTP and WebSocket alike.
  `UsePersistedOperationsEnforcement()` still compiles and adds nothing; earlier versions of this
  sample mapped it in a `UseWhen` branch, which is no longer needed.
- **`GateOperations(roles: "Operator")`.** The management mutations live under `operations`, and a
  host that exposes that namespace with no posture refuses to start. The gate covers the namespace
  only, so the persisted trains on the rest of the endpoint stay reachable. The management
  mutations always bypass enforcement (they cannot be persisted by id), and the gate is what
  protects them.
- **The dev allowlist.** `AllowOperationsMatching(id => id.StartsWith("dev_"))` admits any inline
  document whose operation name starts with `dev_`. The name is chosen by the caller, so outside
  Development it would let anyone run anything: it is registered only in Development.
- **The dashboard.** It refuses to start without a posture. The sample serves it only in
  Development, opened with `AllowAnonymousDashboard()`; anywhere else, register it with
  `RequirePolicy(...)` or `RequireRoles(...)`.

A JSON-array batch is refused by the endpoint with `HC0009` before enforcement sees it, so an
inline document smuggled into a batch beside a persisted id runs nothing.

## Tests

```bash
dotnet test tests/Trax.Samples.PersistedOperations.E2E     # 33 tests against Postgres
```

The suite runs against the `persisted_operations_e2e_tests` database on port 5432
(`TRAX_TEST_PG_PORT` moves the port), cleared of persisted operations before each test. It fails,
rather than skips, when the database is missing.

| Class | Proves |
|---|---|
| `EnforcementTests` | inline documents refused, ids served, the `dev_` carve-out |
| `MutationFlowTests`, `HotFixFlowTests` | upload, deactivate, restore and hot-fix through the GraphQL mutations, with history |
| `AuthorizedSchemaUpsertTests` | uploads validate against a schema carrying `@authorize`, and a gated field stays gated when run by id |
| `ProductionPostureTests` | in Production the `dev_` carve-out and the dashboard are gone; an anonymous upload is refused in every environment; Development serves the dashboard |
| `AdditionalCoverageTests` | variables, content types, batches, tenant scoping, the shape-diff guardrail |

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [Management mutations](/docs/sdk-reference/persisted-operations/management-mutations) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)
