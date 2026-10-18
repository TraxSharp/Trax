---
title: Turn on persisted operations
description: "Make a Trax GraphQL host run only stored documents: register persisted operations, roll out in shadow mode, upload documents, scale to several nodes."
type: how-to
area: api
nav_order: 5
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations
---

# Turn on persisted operations

These steps take a Trax GraphQL host from accepting any query to running only documents stored
under an id. What the contract is and why the host asks how changes reach each node is in
[Persisted operations](/docs/persisted-operations). The
[Persisted Operations sample](/docs/samples/persisted-operations) is a working host.

## 1. Check the prerequisites

- The documents live in the `trax.persisted_operation` table, read and written through the Trax
  data context, so the host needs a data provider such as
  `AddEffects(effects => effects.UsePostgres(...))`. The tables come with the Trax schema
  migrations.
- Install the package:

```bash
dotnet add package Trax.Api.GraphQL.PersistedOperations
```

## 2. Register persisted operations

Start in shadow mode: inline queries still run, and each one is logged, so you can see which
clients would be refused before anyone is.

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .UsePersistedOperations(opts =>
            opts.RequirePersisted(false)
                .LogNonPersistedRequests(true)
                .SingleNode()
                .GateManagement(roles: "Deployer")
        )
);
```

- `SingleNode()` says one process serves the endpoint and writes the store. With more than one
  node, use step 6 instead. The host refuses to start with neither, or with both.
- `GateManagement` serves the management fields, `persistedOperations { ... }` at the root, to a
  role or policy. Without it they are not in the schema. It is a gate of its own: the uploader
  needs this role and nothing under `operations`, which `UsePersistedOperations` does not expose.
  The other choices are in [Authorization rules](/docs/authorization).
- `RequirePersisted(false)` without `LogNonPersistedRequests(true)` does nothing, and the host
  refuses to start.

## 3. Upload the documents your clients send

Each client's build produces a manifest of ids and documents. Upload it before the clients that
use it ship, from CI or a deploy step. Over GraphQL, as a caller the management gate admits:

```graphql
mutation Upload($input: UploadPersistedOperationInput!) {
  persistedOperations {
    uploadPersistedOperation(input: $input) {
      success
      operation { id shapeFingerprint isActive }
      errors { code message }
    }
  }
}
```

```json
{ "input": { "id": "userProfile_v1", "document": "query UserProfile($id: Int!) { user(id: $id) { id name email } }" } }
```

Or in process, from a tool that registers only the store:

```csharp compile
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;
using Trax.Api.GraphQL.PersistedOperations.Services;

builder.Services.AddPersistedOperationStore(store =>
    store.UseRabbitMqInvalidation(rabbitMqConnectionString)
);

var app = builder.Build();
var service = app.Services.GetRequiredService<IPersistedOperationsService>();

var result = await service.UploadAsync(
    new UploadPersistedOperationInput(
        "userProfile_v1",
        "query UserProfile($id: Int!) { user(id: $id) { id name email } }"
    ),
    CancellationToken.None
);

foreach (var error in result.Errors)
    Console.WriteLine($"{error.Code}: {error.Message}");
```

`UseRabbitMqInvalidation` here is the broker the GraphQL nodes use, so they hear of the upload;
`store.SingleNode()` instead when no other process caches the operations. A refused upload comes
back as an error with a code, never as an exception; the codes are on
[Management mutations](/docs/sdk-reference/persisted-operations/management-mutations#error-payload).
An upload that would change the response shape of an existing id is refused with
`SHAPE_DIFF_VIOLATION`; pass `bypassShapeDiff: true` only once you have checked that shipped
clients still read the new shape.

## 4. Watch the logs, then enforce

Run in shadow mode for a full release cycle. Each inline query is logged at Information as
`Trax persisted-operations observed inline query`, with its operation name. When nothing
unexpected is left, turn enforcement on, first on a canary environment, then in production:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .UsePersistedOperations(opts =>
            opts.RequirePersisted(true).SingleNode().GateManagement(roles: "Deployer")
        )
);
```

An inline query is now refused with `PERSISTED_OPERATION_REQUIRED` and HTTP 400. Introspection
still passes; add `.DisableIntrospection()` to refuse it as well.

## 5. Let developer tools run inline queries

In Development, name the operations a playground or smoke test may run inline:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .UsePersistedOperations(opts =>
        {
            opts.RequirePersisted(true).SingleNode().GateManagement(roles: "Deployer");
            if (builder.Environment.IsDevelopment())
                opts.AllowOperations("playground_smoke_test")
                    .AllowOperationsMatching(name => name.StartsWith("dev_"));
        })
);
```

The allowlist matches the name the caller sends, not the document, so keep it to networks you
trust; see [Allowlist and dev carve-outs](/docs/persisted-operations#allowlist-and-dev-carve-outs).

## 6. Run more than one node

When more than one node serves the endpoint, or anything else writes the store, broadcast each
change over RabbitMQ so every node empties its caches:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .UsePersistedOperations(opts =>
            opts.RequirePersisted(true)
                .UseRabbitMqInvalidation(rabbitMqConnectionString)
                .WithCacheMaxAge(TimeSpan.FromMinutes(2))
                .GateManagement(roles: "Deployer")
        )
);
```

- `WithCacheMaxAge` bounds how long a node serves a change it missed. The default is five minutes.
- When a mutation returns `CHANGE_NOT_BROADCAST`, the change is saved and in force on the node that
  made it, and the broker did not confirm the broadcast. Repeat the change to send it again.
- `WithInMemoryCache()` adds a cache of the store's lookups (`IPersistedOperationCache`; a change
  on this node, or one heard from the broker, calls its `Invalidate` for that id). Add it only when
  measurements show the lookup is hot; its TTL may not exceed the maximum age.

Every builder method is on
[PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder).

## 7. Manage operations from the dashboard

With the [dashboard](/docs/dashboard) on the host, a **Persisted Operations** entry appears under
**Data** once the host turns on persisted operations, with `UsePersistedOperations(...)` on a
GraphQL host or `AddPersistedOperationStore(...)` on a dashboard host that serves no GraphQL, and
declares the management gate with `GateManagement(...)`. Without both, the entry is hidden and
`/trax/data/persisted-operations` says management is not served on this server.

The pages hold the operator to the same gate as the `persistedOperations` fields: every read and
write checks the dashboard's own posture, then the gate's policy and roles. An operator at the
dashboard who manages documents needs both, as one at the React dashboard does.

The page lists `trax.persisted_operation` a page at a time, filters by tenant, status and id
prefix, and uploads, edits, deactivates and restores against the row's own tenant. It goes through
the same service as the GraphQL fields, so it refuses what the API refuses, with the same message;
parse, schema and shape errors show inline in the editor, and the editor's **Bypass shape-diff
guardrail** checkbox is the `bypassShapeDiff` flag. A change saved but not broadcast shows as a
warning carrying the `CHANGE_NOT_BROADCAST` message, not as a refusal.

A dashboard in a process of its own declares how its writes reach the GraphQL nodes, the same way
an uploader does:

```csharp compile
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddPersistedOperationStore(store =>
    store.UseRabbitMqInvalidation(rabbitMqConnectionString).GateManagement(roles: "Deployer")
);
```

The dashboard's own setup, including the posture it refuses to start without, is in
[Add the dashboard to a host](/docs/dashboard).

The React operations console keeps working on a host that requires persisted operations: the
host registers the console's own documents when it starts and the console sends them by id. See
[UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard#with-persisted-operations).

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) | [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service) | [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) | [Management mutations](/docs/sdk-reference/persisted-operations/management-mutations) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
