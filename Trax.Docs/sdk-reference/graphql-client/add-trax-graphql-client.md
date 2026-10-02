---
layout: default
title: AddTraxGraphQLClient
description: Reference for AddTraxGraphQLClient, which registers a GraphQL client for one endpoint, the services it adds and its defaults.
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 1
---

# AddTraxGraphQLClient

Registers a GraphQL client against one endpoint and returns a [`TraxGraphQLClientBuilder`](/docs/sdk-reference/graphql-client/builder) for configuring it.

## Signature

```csharp
public static TraxGraphQLClientBuilder AddTraxGraphQLClient(
    this IServiceCollection services,
    Uri baseAddress)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceCollection` | Yes | The service collection |
| `baseAddress` | `Uri` | Yes | The GraphQL endpoint every request is sent to, for example `https://api.example.com/graphql` |

**Returns**: `TraxGraphQLClientBuilder`. Chaining is optional: the registration is complete when this returns.

**Throws**: `ArgumentNullException` when `services` or `baseAddress` is `null`.

## What it registers

Four singletons, all unkeyed:

| Service | Default |
|---------|---------|
| `IGraphQLClientConfiguration` | Built from the builder's options the first time it is resolved |
| `ISchemaProvider` | `IntrospectingSchemaProvider`, which introspects `baseAddress` on first use |
| `IGraphQLClientValidator` | Validates each query against the schema and caches the result per query text |
| `IGraphQLClientExecutor` | Validates, sends and extracts. Resolve this to run requests |

With no chained calls you get introspection, `ResponseStrictness.Lenient`, a new `HttpClient`, and the default JSON options described on [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/builder#configurejson).

The configuration is built lazily, so builder calls made before the container resolves the client take effect. Calls made after it has been resolved do not.

## Example

```csharp
builder.Services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .UseFileSchema("schema.graphql")
    .WithStrictness(ResponseStrictness.ThrowOnDrift);

public class PlayerLookup(IGraphQLClientExecutor graphql)
{
    public Task<PlayerProfile> Get(string id, CancellationToken ct) =>
        graphql.Run(new GetPlayerRequest { Id = id }, ct);
}
```

## Remarks

- A second `AddTraxGraphQLClient` call does not add a second client: its registrations come later, so they win, and both calls share one schema. To talk to more than one server, use [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client).
- The executor is safe to use concurrently. Inject it wherever a request is run.
- Against a Trax server outside Development, introspection is refused unless that server's host allows it with `AllowIntrospection`. Use `UseFileSchema` or `UseAssemblySchema` there.

## Package

```
dotnet add package Trax.Api.GraphQL.Client
```
