---
title: Persisted Operations
description: "The PersistedOperations sample: an app calls by id, an operator changes the answer without a new app, and a breaking fix is refused."
type: explanation
area: samples
nav_order: 8
---

# Persisted Operations

`samples/PersistedOperations` is a GraphQL API that accepts only persisted operations: an app sends
an operation id and its variables, and the server runs the document it stores for that id. A console
client plays the app and the operator through three stories:

1. **A shipped app calls the API by id.** The app sends `greet_v1` and a name, never a query. An
   inline query is refused.
2. **Fix it without shipping a new app.** The stored `greet_v1` asks for `style: CASUAL` and answers
   `Hey Alice!`. The operator re-uploads it with `style: FORMAL`; the same call now answers
   `Good day, Alice.`.
3. **A fix that would break shipped apps is refused.** An upload that renames the field the app
   reads fails the shape-diff guardrail, and `greet_v1` keeps answering as before.

## What it proves

| Feature | Where |
|---|---|
| `RequirePersisted(true)`: every inline document refused with `PERSISTED_OPERATION_REQUIRED` | `Program.cs`, `EnforcementTests`, `HotFixFlowTests` |
| `SingleNode()`: the declaration persisted operations refuse to start without | `Program.cs` |
| The management mutations under `persistedOperations`, gated to one role with `GateManagement` | `ProductionPostureTests`, `MutationFlowTests` |
| An upload that keeps the response shape served on the next call by the same id; one that changes it refused | the Client, `HotFixFlowTests` |
| The demo key, the dashboard and the page only in Development | `Program.cs`, `ProductionPostureTests` |

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

1. The app calls greet_v1 for Alice
   greet_v1 answers: Hey Alice!
   An inline query is refused: {"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]}

2. The operator re-uploads greet_v1 with style FORMAL
   greet_v1 answers: Good day, Alice.

3. Renaming greeting to message is refused: {"code":"SHAPE_DIFF_VIOLATION","message":"Persisted operation 'greet_v1' edit rejected: response shape changed (old fingerprint …, new …). Upload it again with bypassShapeDiff: true if the change is shape-safe."}
   greet_v1 answers: Good day, Alice.
```

Each step, as the client sends it:

- **The app's call** is `{ "id": "greet_v1", "variables": { "name": "Alice" } }` with no credential.
- **The fix** re-uploads `greet_v1` as the operator (`X-Api-Key: operator-key-do-not-use-in-production`)
  with `style: FORMAL` in place of `style: CASUAL`. The response still has one `greeting` string at
  the same path, so the shape fingerprint does not change and the upload needs no bypass.
- **The refused edit** is the same document with `{ message: greeting }`: the app reads
  `greeting`, which would no longer be in the response, so the fingerprint changes and the upload is
  refused. The stored document stays the FORMAL one. `bypassShapeDiff: true` would force it through;
  the client does not.

The client exits non-zero if any answer differs from the one expected, or if the refused edit is
accepted. Its first step re-uploads the manifest, which puts `greet_v1` back to `CASUAL` under the
same fingerprint, so it can run any number of times.

The API also serves a page at http://localhost:5240 that plays the same three stories: pick one and
press Play. It shows each request with its answer and how long it took, the greeting the app shows,
and what the server stores for `greet_v1`. Its operator uploads send the demo key, so the page is
served only in Development, like the key.

## Try it

```bash
G=http://localhost:5240/trax/graphql/

# An inline document is refused
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"query { discover { greeting { greet(input: { name: \"Eve\", style: FORMAL }) { greeting } } } }"}'
# {"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]}

# The same operation by id (after the client has uploaded the manifest)
curl -s $G -H 'Content-Type: application/json' \
  -d '{"id":"greet_v1","variables":{"name":"Eve"}}'
# {"data":{"discover":{"greeting":{"greet":{"greeting":"Hey Eve!"}}}}}

# An upload without the operator key is refused
curl -s $G -H 'Content-Type: application/json' \
  -d '{"query":"mutation Upload($input: UploadPersistedOperationInput!) { persistedOperations { uploadPersistedOperation(input: $input) { success } } }","variables":{"input":{"id":"x_v1","document":"{ __typename }"}}}'
# {"errors":[{"message":"Not authorized.","path":["persistedOperations"],"extensions":{"code":"TRAX_AUTHORIZATION"}}],"data":{"persistedOperations":null}}
```

The dashboard is at http://localhost:5240/trax, with the stored operations under
**Data > Persisted Operations**.

## How it works

### Layout

```
samples/PersistedOperations/
├── Trax.Samples.PersistedOperations/          GreetTrain: a name and a style in, a greeting out
├── Trax.Samples.PersistedOperations.Api/      the host (Program.cs) and the page in wwwroot/
└── Trax.Samples.PersistedOperations.Client/   console client and manifest.json: the three stories
```

The train behind every document takes a name and a `GreetingStyle` (`Casual` or `Formal`, `CASUAL` and
`FORMAL` in GraphQL) and returns a `GreetOutput` with one `Greeting` string:

```csharp from=Trax.Samples/samples/PersistedOperations/Trax.Samples.PersistedOperations/Trains/Greeting/Greet/GreetInput.cs
public record GreetInput : IManifestProperties
{
    public required string Name { get; init; }

    public GreetingStyle Style { get; init; }
}
```

```csharp from=Trax.Samples/samples/PersistedOperations/Trax.Samples.PersistedOperations/Trains/Greeting/Greet/Junctions/ComposeGreetingJunction.cs
public class ComposeGreetingJunction : Junction<GreetInput, GreetOutput>
{
    public override Task<GreetOutput> Run(GreetInput input) =>
        Task.FromResult(
            new GreetOutput
            {
                Greeting = input.Style switch
                {
                    GreetingStyle.Formal => $"Good day, {input.Name}.",
                    _ => $"Hey {input.Name}!",
                },
            }
        );
}
```

`Client/manifest.json` ships one operation. The style is fixed in the stored document, not sent by
the app, so changing it is the operator's job:

```json from=Trax.Samples/samples/PersistedOperations/Trax.Samples.PersistedOperations.Client/manifest.json
{
  "operations": [
    {
      "id": "greet_v1",
      "document": "query Greet($name: String!) { discover { greeting { greet(input: { name: $name, style: CASUAL }) { greeting } } } }"
    }
  ]
}
```

### The host

`Program.cs`, less its banner comment:

```csharp from=Trax.Samples/samples/PersistedOperations/Trax.Samples.PersistedOperations.Api/Program.cs
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.PersistedOperations;
using Trax.Samples.PersistedOperations.Api.Auth;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? "Host=localhost;Port=5432;Database=trax_persisted_operations;Username=trax;Password=trax123";

var isDevelopment = builder.Environment.IsDevelopment();

// The demo operator key is published in this repository, so it is registered only in
// Development. Anywhere else no credential exists until you register a real scheme, so every
// upload is refused.
if (isDevelopment)
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", DemoKeys.OperatorRole)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// The stored documents live in Postgres. The dashboard's pages need the scheduler services even
// though this sample queues no work.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(GraphQLNamespaces).Assembly)
        .AddScheduler(scheduler => scheduler)
);

builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(opts =>
        opts.RequirePersisted(true)
            .LogNonPersistedRequests(true)
            // One process serves this endpoint and writes the store, so an upload reaches
            // every cached copy without a broadcast. A second node needs
            // UseRabbitMqInvalidation(...) instead; the host refuses to start with neither.
            .SingleNode()
            // The upload mutations live under `persistedOperations`, behind their own gate.
            // Gate them to the operator; greet stays reachable to the app.
            .GateManagement(roles: DemoKeys.OperatorRole)
    )
);

// The dashboard shows the stored documents under Data > Persisted Operations. It refuses to start
// without a posture: this sample serves it only in Development, open. Anywhere else, register it
// with RequirePolicy(...) or RequireRoles(...).
if (isDevelopment)
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

var app = builder.Build();

// The page at / (wwwroot) plays the three stories against this API. Its operator uploads send the
// demo key, so it is served only in Development, with the key.
if (isDevelopment)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseTraxGraphQL();
if (isDevelopment)
    app.UseTraxDashboard();

app.Run();

/// <summary>
/// Visible to <c>WebApplicationFactory&lt;Program&gt;</c> for E2E tests.
/// </summary>
public partial class Program;
```

`DemoKeys` (in `Auth/DemoKeys.cs`) holds two constants: `OperatorKey =
"operator-key-do-not-use-in-production"` and `OperatorRole = "Operator"`.

The host references `Trax.Api`, `Trax.Api.Auth.ApiKey`, `Trax.Api.GraphQL`,
`Trax.Api.GraphQL.PersistedOperations`, `Trax.Dashboard`, `Trax.Scheduler` and
`Trax.Effect.Provider.Json`; `Trax.Effect.Data.Postgres` and `Trax.Mediator` come in through the
trains library. A host that calls `UseTraxDashboard()` also sets
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
- **`GateManagement(roles: "Operator")`.** The management mutations live under
  `persistedOperations` at the root, and they are in the schema only once a gate is declared for
  them. The gate covers those fields only, so `greet` stays reachable to the app, which carries no
  credential, and the host exposes nothing under `operations`. The management mutations always
  bypass enforcement (they cannot be persisted by id), and the gate is what protects them.
- **The demo key.** It is published in this repository, so `AddTraxApiKeyAuth` registers it only in
  Development. Anywhere else no credential exists until you register a real scheme, so every upload
  is refused.
- **The dashboard.** It refuses to start without a posture. The sample serves it only in
  Development, opened with `AllowAnonymousDashboard()`; anywhere else, register it with
  `RequirePolicy(...)` or `RequireRoles(...)`.

A JSON-array batch is refused by the endpoint with `HC0009` before enforcement sees it, so an
inline document smuggled into a batch beside a persisted id runs nothing.

## Tests

```bash
dotnet test tests/Trax.Samples.PersistedOperations.E2E     # against Postgres
```

The suite runs against the `persisted_operations_e2e_tests` database on port 5432
(`TRAX_TEST_PG_PORT` moves the port), cleared of persisted operations before each test. It fails,
rather than skips, when the database is missing.

| Class | Proves |
|---|---|
| `HotFixFlowTests` | the three stories: the call by id and the inline refusal, the FORMAL fix with no bypass, the refused rename with the old answer standing, and the manifest restoring CASUAL; with history, deactivate and restore |
| `EnforcementTests` | inline documents refused, ids served, an unknown id reported |
| `MutationFlowTests` | upload, schema-mismatch refusal, shape-diff refusal and bypass, deactivate, all through the GraphQL mutations |
| `ProductionPostureTests` | in Production an inline document is refused and the dashboard and the page are gone; an anonymous upload is refused in every environment; Development serves the dashboard and the page, which shadows neither the dashboard nor the endpoint |
| `AdditionalCoverageTests` | variables, content types, batches, tenant scoping, the shape-diff guardrail |

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [Management mutations](/docs/sdk-reference/persisted-operations/management-mutations) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)
