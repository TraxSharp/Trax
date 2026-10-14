---
title: IGraphQLClientConfiguration
description: "Reference for IGraphQLClientConfiguration, the settings a GraphQL client runs with, and GraphQLClientConfigurationBuilder, the options it is built from."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 13
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Configuration/IGraphQLClientConfiguration.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Configuration/GraphQLClientConfigurationBuilder.cs
---

# IGraphQLClientConfiguration

The settings one GraphQL client runs with: the endpoint, the HTTP and JSON plumbing, and how
strictly responses are checked. Built once from a `GraphQLClientConfigurationBuilder` the first
time it is resolved, and registered as a singleton (keyed, for a keyed client).

## Signature

```csharp fragment
public interface IGraphQLClientConfiguration
{
    Uri BaseAddress { get; }
    HttpClient HttpClient { get; }
    GraphQLHttpClient GraphQLHttpClient { get; }
    IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; }
    JsonSerializerOptions JsonSerializerOptions { get; }
    GraphQLHttpClientOptions GraphQLClientOptions { get; }
    bool DisposeHttpClient { get; }
    bool RemoveSubscriptionsFromSchema { get; }
    ResponseStrictness ResponseStrictness { get; }
}

public class GraphQLClientConfigurationBuilder
{
    public GraphQLClientConfigurationBuilder(Uri baseAddress);
    public IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; set; }
    public JsonSerializerOptions JsonSerializerOptions { get; set; }
    public GraphQLHttpClientOptions GraphQLClientOptions { get; set; }
    public HttpClient? HttpClient { get; set; }
    public bool DisposeHttpClient { get; set; }
    public bool RemoveSubscriptionsFromSchema { get; set; }
    public ResponseStrictness ResponseStrictness { get; set; }
    public IGraphQLClientConfiguration Build();
}
```

## Properties

| Property | Description |
|----------|-------------|
| `BaseAddress` | The GraphQL endpoint every request is sent to |
| `HttpClient` | The HTTP client requests go through: the `IHttpClientFactory` client named for this GraphQL client, or the one supplied. Its `BaseAddress` is not used or changed |
| `GraphQLHttpClient` | The GraphQL.Client HTTP client built over `HttpClient`, used for requests and introspection |
| `WebsocketJsonSerializer` | The serializer the GraphQL.Client transport uses for request and response envelopes |
| `JsonSerializerOptions` | The options responses are deserialized with, which response-shape checks also use to name properties |
| `GraphQLClientOptions` | The GraphQL.Client options the HTTP client was built with. Their `EndPoint` is set to `BaseAddress` |
| `DisposeHttpClient` | Whether disposing the configuration also disposes `HttpClient` |
| `RemoveSubscriptionsFromSchema` | Whether every schema provider drops the subscription type before queries are validated |
| `ResponseStrictness` | How strictly responses are checked against the request's response type |

## GraphQLClientConfigurationBuilder

The mutable options `AddTraxGraphQLClient` creates and
[TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder) sets.
`TraxGraphQLClientBuilder.Configure` hands it out for options without a method of their own.

| Property | Default | Set by |
|----------|---------|--------|
| `WebsocketJsonSerializer` | GraphQL.Client's `SystemTextJsonSerializer` | `Configure` |
| `JsonSerializerOptions` | `PropertyNameCaseInsensitive = true`, a `JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)` (so `GOLD_TIER` reads into `GoldTier`) and a `DateOnly` converter | `ConfigureJson` |
| `GraphQLClientOptions` | `new GraphQLHttpClientOptions()` | `Configure` |
| `HttpClient` | `null`: the client `IHttpClientFactory` creates under the client's name | `ConfigureHttpClient` |
| `DisposeHttpClient` | `false` | `DisposeHttpClient()` |
| `RemoveSubscriptionsFromSchema` | `true` | `Configure` |
| `ResponseStrictness` | `Lenient` | `WithStrictness` |

## Behavior

- The constructor throws `ArgumentNullException` for a `null` address.
- `Build()` creates a configuration from the current options. With no `HttpClient` set it creates a
  long-lived one of its own, whose pooled connections are replaced every two minutes, and disposes it
  with the configuration. A client registered with `AddTraxGraphQLClient` gets its `HttpClient` from
  `IHttpClientFactory` instead.
- `Build()` throws `InvalidOperationException` when `GraphQLClientOptions.EndPoint` is set to an
  address other than the client's. Leave it unset.
- The registered configuration is built when first resolved; changes to the builder after that are
  not seen. Disposing it disposes the `GraphQLHttpClient`, and `HttpClient` too when
  `DisposeHttpClient` is set.

## Example

```csharp compile
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .Configure(o =>
    {
        o.RemoveSubscriptionsFromSchema = false;
        o.ResponseStrictness = ResponseStrictness.WarnOnDrift;
    });
```

## See also

- [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder)
- [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness)
- [ISchemaProvider](/docs/sdk-reference/graphql-client/i-schema-provider)
