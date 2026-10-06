---
layout: default
title: GraphQL Client
description: "Index of the Trax.Api.GraphQL.Client reference: registration, the builder, request types, response strictness and startup validation of queries."
parent: SDK Reference
nav_order: 10
has_children: true
---

# GraphQL Client

Reference for `Trax.Api.GraphQL.Client`, the outbound client that validates hand-written queries against a server's schema and runs them, and for `Trax.Api.GraphQL.Client.Trax`, which adds an in-process schema source and startup validation. For the concepts (query modes, schema providers, keyed clients), see [GraphQL Client](/docs/api-graphql-client).

```csharp
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .UseFileSchema("schema.graphql")
    .WithStrictness(ResponseStrictness.ThrowOnDrift);

// Later, from DI:
var player = await executor.Run(new GetPlayerRequest { Id = "p-1" }, cancellationToken);
```

## Pages

| Page | Description |
|------|-------------|
| [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) | Registers a client against one endpoint and returns its builder |
| [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client) | Registers a client under a DI key, for talking to several servers |
| [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/builder) | Schema source, strictness, HTTP client, JSON options, startup validation |
| [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-assemblies) | Validates every request type in a set of assemblies against the schema |
| [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request) | The request contract, `IGraphQLClientExecutor.Run`, and the exceptions it throws |
| [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request) | Base class for requests whose query is an embedded `.graphql` file |
| [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness) | How strictly a response is checked against the request's response type |

## Packages

```
dotnet add package Trax.Api.GraphQL.Client
dotnet add package Trax.Api.GraphQL.Client.Trax   # UseAssemblySchema, UseStartupValidation
dotnet add package Trax.Api.GraphQL.Client.Typed  # TypedRequest (POCO-derived queries)
```

The kernel package has no dependency on trains, the mediator or a database, so any .NET application can use it.
