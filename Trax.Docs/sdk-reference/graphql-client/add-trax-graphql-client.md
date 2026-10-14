---
title: AddTraxGraphQLClient
description: "Reference for AddTraxGraphQLClient, which registers a GraphQL client for one endpoint, the services it adds and its defaults."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 1
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Extensions/ServiceExtensions.cs
---

# AddTraxGraphQLClient

Registers a GraphQL client against one endpoint and returns a
[TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder) for
configuring it. Declared on the static class `ServiceExtensions`.

## Signature

```csharp fragment
public static TraxGraphQLClientBuilder AddTraxGraphQLClient(
    this IServiceCollection services,
    Uri baseAddress)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceCollection` | Yes | The service collection |
| `baseAddress` | `Uri` | Yes | The GraphQL endpoint every request is sent to, for example `https://api.example.com/graphql` |

## Returns

`TraxGraphQLClientBuilder`. The registration is complete when this returns; chaining is optional.

## Behavior

- Throws `ArgumentNullException` when `services` or `baseAddress` is `null`.
- With no chained calls the client introspects `baseAddress` for its schema, uses
  `ResponseStrictness.Lenient`, sends through an `IHttpClientFactory` client, and deserializes with
  the default JSON options listed on
  [IGraphQLClientConfiguration](/docs/sdk-reference/graphql-client/graphql-client-configuration#graphqlclientconfigurationbuilder).
- The configuration is built the first time it is resolved, so builder calls made before that take
  effect and calls made after it do not.
- A second `AddTraxGraphQLClient` call does not add a second client: its registrations come later and
  win. A client per server is
  [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client).
- The executor is a singleton and safe to use concurrently.
- A Trax server outside Development refuses introspection unless its host admits the client with
  `AllowIntrospection`, so against one the schema comes from `UseFileSchema` or `UseAssemblySchema`.

### Registered services

| Service | Lifetime | Default |
|---------|----------|---------|
| `IGraphQLClientConfiguration` | Singleton | Built from the builder's options when first resolved |
| `ISchemaProvider` | Singleton | `IntrospectingSchemaProvider`, which introspects `baseAddress` on first use |
| `IGraphQLClientValidator` | Singleton | Validates each query against the schema and caches the result per query text |
| `IGraphQLClientExecutor` | Singleton | Validates, sends and extracts. Resolve this to run requests |
| Named `HttpClient` `Trax.Api.GraphQL.Client` | `IHttpClientFactory` | A `SocketsHttpHandler` whose pooled connections are replaced every two minutes, with an infinite handler lifetime. Reachable through `TraxGraphQLClientBuilder.HttpClientBuilder` |

## Example

```csharp compile
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

## See also

- [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client)
- [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder)
- [IGraphQLClientExecutor](/docs/sdk-reference/graphql-client/i-graphql-client-executor)
- [GraphQL Client](/docs/api-graphql-client)
