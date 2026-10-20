---
title: Call another GraphQL server
description: "Call a GraphQL server from .NET with Trax.Api.GraphQL.Client: write a request, register the client, validate queries at startup, run them."
type: how-to
area: api
nav_order: 1
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client
  - Trax.Api/src/Trax.Api.GraphQL.Client.Trax
  - Trax.Api/src/Trax.Api.GraphQL.Client.Typed
redirect_from:
  - /docs/api-graphql-client
---

# Call another GraphQL server

`Trax.Api.GraphQL.Client` sends hand-written queries to a GraphQL endpoint and checks each one
against the server's schema before it goes out. It works in any .NET application; nothing in it
needs trains, the mediator or a database. Why it validates instead of generating code is in
[How the GraphQL client checks queries](/docs/api/graphql-client-validation).

## 1. Install the packages

```bash
dotnet add package Trax.Api.GraphQL.Client
dotnet add package Trax.Api.GraphQL.Client.Trax    # optional: UseAssemblySchema, UseStartupValidation
dotnet add package Trax.Api.GraphQL.Client.Typed   # optional: queries generated from a POCO
```

## 2. Write a request

A request is a class implementing `IGraphQLClientRequest<TResponse>`: the query text, its
variables, and the type the result field is read into.

```csharp compile=client
using Trax.Api.GraphQL.Client;

public sealed record PlayerProfile(string Id, string Name, int Level);

public sealed class GetPlayerRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query =>
        """
        query GetPlayer($id: String!) {
          player(id: $id) { id name level }
        }
        """;

    public object Variables => new { id = Id };
}
```

A document holds exactly one operation; fragments may come before or after it. Two other ways to
write the same request:

- Put the query in a `.graphql` file compiled as an `EmbeddedResource` and derive from
  [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request), which
  also describes how the file is found.
- Derive from `TypedRequest<TResponse>` (in `.Typed`) and let the query be generated from the
  response type's properties. See [Typed requests](/docs/api/typed-requests).

### Nested envelopes (Path)

A Trax server puts a namespaced train's field under `discover { <namespace> { ... } }`. A typed
request reaches it with `Path = "discover.<namespace>"` on `[GraphQLOperation]`; see
[GraphQLOperation properties](/docs/api/typed-requests#graphqloperation-properties).

## 3. Register the client and pick a schema source

```csharp compile=client
using Trax.Api.GraphQL.Client;

builder
    .Services.AddTraxGraphQLClient(new Uri("https://players.example.com/graphql"))
    .UseFileSchema("schemas/players.graphql");
```

### Schema providers

Choose where the schema comes from:

| Call | Use it when |
|---|---|
| none, or `UseIntrospection()` | The endpoint answers introspection from this caller. A Trax server does so only in Development, unless its host admits the caller through `AllowIntrospection`. |
| `UseFileSchema(path)` | You check in an SDL snapshot of the server. The usual choice against a production Trax server. |
| `UseAssemblySchema(configure)` (`.Trax`) | The server's HotChocolate configuration is in a .NET assembly you can reference. |

The calls are on [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder).

## 4. Add authentication and timeouts

The client sends through a named `HttpClient` from `IHttpClientFactory`. Configure it through the
builder's `HttpClientBuilder` as you would any named client:

```csharp compile
using Trax.Api.GraphQL.Client;

builder
    .Services.AddTraxGraphQLClient(new Uri("https://players.example.com/graphql"))
    .UseFileSchema("schemas/players.graphql")
    .HttpClientBuilder.AddHttpMessageHandler<BearerTokenHandler>();
```

`BearerTokenHandler` stands for your own `DelegatingHandler`, registered in the container. A
timeout goes on the same `IHttpClientBuilder`, with
`ConfigureHttpClient(http => http.Timeout = TimeSpan.FromSeconds(30))`.
Nothing is retried by default, because every request is a POST and a mutation is not safe to
repeat. If you add `AddStandardResilienceHandler()` from Microsoft.Extensions.Http.Resilience,
call `DisableForUnsafeHttpMethods()` on its retry options, or add it only to clients that send
queries.

## 5. Validate requests at startup

With `.Trax`, chain `UseStartupValidation` and the host refuses to start when a request no longer
matches the schema:

```csharp compile
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

builder
    .Services.AddTraxGraphQLClient(new Uri("https://players.example.com/graphql"))
    .UseFileSchema("schemas/players.graphql")
    .UseStartupValidation(typeof(Program).Assembly);
```

Without `.Trax`, call it yourself after the app is built, or from a test:

```csharp compile
using Trax.Api.GraphQL.Client;

var app = builder.Build();
await app.Services.ValidateGraphQLClientAssembliesAsync(typeof(Program).Assembly);
```

Which request types each call checks, and what it throws, is on
[ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies).

## 6. Run a request

Inject `IGraphQLClientExecutor` and pass it the request:

```csharp compile=client
using Trax.Api.GraphQL.Client;

public sealed class PlayerLookup(IGraphQLClientExecutor graphql)
{
    public Task<PlayerProfile> Find(string id, CancellationToken cancellationToken) =>
        graphql.Run(new GetPlayerRequest { Id = id }, cancellationToken);
}
```

A query the schema rejects throws `GraphQLValidationException` before any HTTP call. The other
exceptions `Run` throws are listed on
[IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-executor).

## 7. Call several servers

Register each server under a key with `AddKeyedTraxGraphQLClient`, and mark each request with the
key of the client it belongs to, so each client validates only its own requests:

```csharp compile=keyed
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

builder
    .Services.AddKeyedTraxGraphQLClient("billing", new Uri("https://billing.example.com/graphql"))
    .UseFileSchema("schemas/billing.graphql")
    .UseStartupValidation(typeof(Invoice).Assembly);

builder
    .Services.AddKeyedTraxGraphQLClient("shipping", new Uri("https://shipping.example.com/graphql"))
    .UseFileSchema("schemas/shipping.graphql")
    .UseStartupValidation(typeof(Invoice).Assembly);
```

```csharp compile=keyed
using Trax.Api.GraphQL.Client;

public sealed record Invoice(string Id, int TotalCents);

[GraphQLClient("billing")]
public sealed class GetInvoiceRequest : IGraphQLClientRequest<Invoice>
{
    public required string Id { get; init; }

    public string Query => "query GetInvoice($id: String!) { invoice(id: $id) { id totalCents } }";

    public object Variables => new { id = Id };
}

public sealed class InvoiceLookup([FromKeyedServices("billing")] IGraphQLClientExecutor billing)
{
    public Task<Invoice> Find(string id, CancellationToken cancellationToken) =>
        billing.Run(new GetInvoiceRequest { Id = id }, cancellationToken);
}
```

Each key gets its own configuration, `HttpClient`, schema provider and validator cache. A second
unkeyed `AddTraxGraphQLClient` call replaces the first rather than adding a client. Validation
refuses to start when a client finds no request marked for it, or when a request is marked with a
key no client is registered under; see
[AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client).

## 8. Catch response drift

To fail when the response and the response type disagree:

```csharp compile
using Trax.Api.GraphQL.Client;

builder
    .Services.AddTraxGraphQLClient(new Uri("https://players.example.com/graphql"))
    .WithStrictness(ResponseStrictness.ThrowOnDrift);
```

`WarnOnDrift` logs instead of throwing. What is compared is on
[ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness).

## 9. Point the client at a test server

In an end-to-end test, send through the `HttpClient` a `WebApplicationFactory` creates:

```csharp fragment
services
    .AddTraxGraphQLClient(new Uri("http://localhost/trax/graphql"))
    .ConfigureHttpClient(factory.CreateClient());
```

Requests still go to the address passed to `AddTraxGraphQLClient`, whatever the `HttpClient`'s
`BaseAddress`. The [GraphQL Client sample](/docs/samples/graphql-client) does this in its E2E
suite, against two Trax servers with a keyed client each.

## SDK Reference

> [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) | [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client) | [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder) | [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies) | [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request) | [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request) | [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness) | [Typed requests](/docs/api/typed-requests)
