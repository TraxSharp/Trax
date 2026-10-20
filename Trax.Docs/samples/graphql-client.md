---
title: GraphQL Client
description: "The GraphQLClient sample: Trax servers called through keyed Trax GraphQL clients, schema validation before any request, and the three query modes."
type: explanation
area: samples
nav_order: 7
---

# GraphQL Client

`samples/GraphQLClient` in [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) calls GraphQL
servers from .NET code with `Trax.Api.GraphQL.Client`. Each request is validated against the
server's schema before it is sent, so a query the server cannot answer fails in the caller, with no
HTTP round trip. Everything runs in memory: no database, no broker.

## What it proves

| Feature | Where |
|---|---|
| One consumer, two Trax servers with different schemas, through two keyed clients in one container | `Gateway/Program.cs`, `KeyedClientE2ETests` |
| A query written for one server refused by the other's client with `GraphQLValidationException`, before any HTTP call | `KeyedClientE2ETests` |
| Typed requests reaching a Trax namespace with `Path = "discover.<namespace>"` | `Gateway/Requests/KeyedRequests.cs` |
| The three ways to write a request: raw string (A), embedded `.graphql` resource (E), typed POCO (D) | `Trax.Samples.GraphQLClient/Requests/` |
| A client validating against the server's own schema configuration (`UseAssemblySchema`), so the two cannot drift | `Trax.Samples.GraphQLClient/Program.cs` |

## Run

From the `Trax.Samples` root:

```bash
dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient.Gateway
dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient
```

The inventory and billing servers can also run on their own in Development
(`dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient.BillingServer`), the inventory
server on 5313 and the billing server on 5314, serving GraphQL at `/trax/graphql` and health at
`/trax/health`.

## Try it

The Gateway starts both servers on ports 5310 and 5311 and prints:

```text
serverB (inventory) -> Mechanical Keyboard, 42 on hand
serverC (billing)   -> invoice INV-1: 4999c (Paid)
serverB rejected a billing query (schema isolation holds)
```

The modes sample serves its HotChocolate server on 5312 and ends with:

```text
  A == E (raw vs resource) : True
  A ~ D (raw vs typed)     : True
```

It exits with code 1 when either comparison is `False`.

## How it works

### Layout

```
samples/GraphQLClient/
├── Trax.Samples.GraphQLClient.InventoryServer/   Trax server: discover { inventory { getProduct } }
├── Trax.Samples.GraphQLClient.BillingServer/     Trax server: discover { billing { getInvoice } }
├── Trax.Samples.GraphQLClient.Gateway/           starts both, calls each through its keyed client
└── Trax.Samples.GraphQLClient/                   a HotChocolate server and a client in modes A, E, D
```

### The servers are ordinary Trax hosts

```csharp from=Trax.Samples/samples/GraphQLClient/Trax.Samples.GraphQLClient.BillingServer/BillingServerHost.cs
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;
// ...
public static WebApplication Build(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
// ...
    builder.Services.AddAuthorization();

    builder.Services.AddTrax(trax =>
        trax.AddEffects(effects => effects.UseInMemory().AddJson())
            .AddMediator(mediator =>
                mediator
                    .ScanAssemblies(typeof(IGetInvoiceTrain).Assembly)
                    .AllowMissingAuthorizationService()
            )
    );
// ...
    builder.Services.AddTraxGraphQL();
    builder.Services.AddHealthChecks().AddTraxHealthCheck();

    var app = builder.Build();
    app.UseTraxGraphQL();
    app.MapHealthChecks("/trax/health");
    return app;
}
```

`BillingServerHost.Build` is shared by the billing server's own `Program.cs`, the Gateway and the
E2E suite; `InventoryServerHost` has the same shape. The one billing train is `[TraxAllowAnonymous]` and `[TraxQuery(Namespace = "billing")]`, which serves it at
`discover { billing { getInvoice(input: ...) } }`. The client validates against the schema it fetches
by introspection, and a Trax server answers introspection only in Development unless the host passes
`AllowIntrospection` a predicate. These servers keep that default. The Gateway's launch profile runs it,
and so the two servers it starts in-process, in Development; started in Production, the servers refuse
introspection and the Gateway stops with `GraphQLSchemaIntrospectionException`. Against a production
server the client uses `UseFileSchema` or `UseAssemblySchema` instead, or the server admits only the
callers it trusts. See [GraphQL Client: Schema Providers](/docs/api/call-another-graphql-server#schema-providers).

### Two keyed clients in one container

```csharp from=Trax.Samples/samples/GraphQLClient/Trax.Samples.GraphQLClient.Gateway/Program.cs
using Trax.Api.GraphQL.Client;
// ...
var consumer = Host.CreateApplicationBuilder();
consumer.Services.AddKeyedTraxGraphQLClient("serverB", new Uri($"{inventoryUrl}/trax/graphql"));
consumer.Services.AddKeyedTraxGraphQLClient("serverC", new Uri($"{billingUrl}/trax/graphql"));
using var consumerHost = consumer.Build();
var inventoryClient = consumerHost.Services.GetRequiredKeyedService<IGraphQLClientExecutor>(
    "serverB"
);
var billingClient = consumerHost.Services.GetRequiredKeyedService<IGraphQLClientExecutor>(
    "serverC"
);

var product = await inventoryClient.Run(
    new GetProductRequest { Input = new GetProductInput("SKU-1") }
);
// ...
try
{
    await inventoryClient.Run(new GetInvoiceRequest { Input = new GetInvoiceInput("INV-1") });
    Console.WriteLine("UNEXPECTED: the inventory client accepted a billing query");
}
catch (GraphQLValidationException)
{
    Console.WriteLine("serverB rejected a billing query (schema isolation holds)");
}
```

`inventoryUrl` and `billingUrl` are `http://localhost:5310` and `http://localhost:5311`, where the
Gateway starts the two servers in-process before it builds the consumer. The key names the server;
each executor validates against its own server's schema. Running `GetInvoiceRequest` through the
inventory executor throws `GraphQLValidationException`, because the inventory schema has no
`getInvoice`.

### A typed request into a Trax namespace

```csharp from=Trax.Samples/samples/GraphQLClient/Trax.Samples.GraphQLClient.Gateway/Requests/KeyedRequests.cs
using Trax.Api.GraphQL.Client.Typed;
// ...
[GraphQLType("GetProductOutput")]
public sealed record ProductView(string Sku, string Name, int QuantityOnHand);

[GraphQLOperation(OperationType.Query, Path = "discover.inventory", RootField = "getProduct")]
public sealed class GetProductRequest : TypedRequest<ProductView>
{
    [GraphQLArgument("GetProductInput!", VariableName = "input")]
    public required GetProductInput Input { get; init; }
}
```

The record's properties are the selection set; `Path` wraps the root field in the `discover` and
namespace levels Trax generates, and the response is unwrapped along the same path. See
[GraphQL Client: Nested envelopes](/docs/api/typed-requests#graphqloperation-properties).

## Tests

```bash
dotnet test tests/Trax.Samples.GraphQLClient.E2E
```

`KeyedClientE2ETests` boots both Trax servers with `WebApplicationFactory` and builds one container
with two keyed clients, each handed its server's in-process `HttpClient` through
`.ConfigureHttpClient(factory.CreateClient())`: each key queries its own server, unknown ids fall
back, and a query sent through the wrong key fails schema validation. `IntrospectionPostureE2ETests`
checks that both servers serve their schema in Development and refuse introspection in Production.
`ClientModesE2ETests` runs the modes sample's raw-string, resource and typed requests, flat and through
`discover.players`, against its schema and checks that they return the same player.

## SDK Reference

> [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) | [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client) | [Builder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder) | [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
