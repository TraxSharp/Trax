---
title: TraxGraphQLClientBuilder
description: "Reference for TraxGraphQLClientBuilder: schema source, response strictness, the HTTP client, JSON options and direct access to the client's options."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 3
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Configuration/TraxGraphQLClientBuilder/Builder.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Configuration/TraxGraphQLClientBuilder/Builder.Schema.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Configuration/TraxGraphQLClientBuilder/Builder.Options.cs
redirect_from:
  - /docs/sdk-reference/graphql-client/builder
---

# TraxGraphQLClientBuilder

The fluent builder returned by
[AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) and
[AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client). Each
method changes the registration in place and returns the builder, so the registration is
consistent with the calls made so far. On a keyed builder every method applies to that key only.
`Trax.Api.GraphQL.Client.Trax` adds [UseAssemblySchema](/docs/sdk-reference/graphql-client/use-assembly-schema)
and [UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation) as extension
methods.

## Signature

```csharp fragment
public sealed partial class TraxGraphQLClientBuilder
{
    public IHttpClientBuilder HttpClientBuilder { get; }
    public IServiceCollection Services { get; }

    public TraxGraphQLClientBuilder UseIntrospection();
    public TraxGraphQLClientBuilder UseFileSchema(string sdlPath);
    public TraxGraphQLClientBuilder WithStrictness(ResponseStrictness strictness);
    public TraxGraphQLClientBuilder ConfigureHttpClient(HttpClient httpClient);
    public TraxGraphQLClientBuilder DisposeHttpClient(bool dispose = true);
    public TraxGraphQLClientBuilder ConfigureJson(JsonSerializerOptions options);
    public TraxGraphQLClientBuilder Configure(Action<GraphQLClientConfigurationBuilder> mutate);
}
```

## Members

| Member | Description |
|--------|-------------|
| `HttpClientBuilder` | The named `IHttpClientFactory` client this GraphQL client sends through, one per client. Add handlers, resilience or timeouts to it the standard way. Its primary handler is a `SocketsHttpHandler` whose pooled connections are replaced every two minutes. Not used once `ConfigureHttpClient` has supplied a client |
| `Services` | The `IServiceCollection` the client was registered against |
| [`UseIntrospection()`](#useintrospection) | Validate against the schema the endpoint reports by introspection. The default |
| [`UseFileSchema(sdlPath)`](#usefileschema) | Validate against an SDL file |
| [`WithStrictness(strictness)`](#withstrictness) | How strictly responses are checked |
| [`ConfigureHttpClient(httpClient)`](#configurehttpclient) | Send through an `HttpClient` you own |
| [`DisposeHttpClient(dispose)`](#disposehttpclient) | Dispose that `HttpClient` with the client |
| [`ConfigureJson(options)`](#configurejson) | Replace the options responses are deserialized with |
| [`Configure(mutate)`](#configure) | Set any option on the underlying `GraphQLClientConfigurationBuilder` |

## UseIntrospection

Replaces the schema provider with `IntrospectingSchemaProvider`, which sends a standard
introspection query to the endpoint. It is the provider registered when no schema method is
called. The schema methods (`UseIntrospection`, `UseFileSchema`, `UseAssemblySchema`) replace one
another: the last one called decides. A Trax server answers introspection only in Development
unless its host admits the client with `AllowIntrospection`. See
[ISchemaProvider](/docs/sdk-reference/graphql-client/i-schema-provider).

## UseFileSchema

| Parameter | Type | Description |
|-----------|------|-------------|
| `sdlPath` | `string` | An absolute path, or one relative to the process's working directory, of an SDL file such as `schema.graphql` |

Replaces the schema provider with `FileSchemaProvider`. The file is read when the schema is first
requested, not at registration. A missing, empty or invalid file throws
`GraphQLSchemaIntrospectionException` from the validation that asked for it. Throws
`ArgumentException` when `sdlPath` is null, empty or whitespace.

## WithStrictness

Sets `ResponseStrictness`, how the executor treats a response whose fields differ from the response
type's properties. Default `Lenient`. See
[ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness).

## ConfigureHttpClient

Sends requests and introspection through `httpClient` instead of the `IHttpClientFactory` client.
The client is not changed: its `BaseAddress` is neither read nor set, and requests go to the URI the
client was registered with, so one `HttpClient` can serve several GraphQL clients. It is not
disposed with the client unless `DisposeHttpClient()` is called. Throws `ArgumentNullException` when
`httpClient` is `null`.

## DisposeHttpClient

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `dispose` | `bool` | `true` | Whether disposing the client's configuration also disposes the `HttpClient` passed to `ConfigureHttpClient` |

Without the call, only the GraphQL.Client wrapper around the supplied `HttpClient` is disposed.

## ConfigureJson

Replaces the `JsonSerializerOptions` responses are deserialized with. The supplied options replace
the defaults (case-insensitive property names, a `JsonStringEnumConverter` with
`JsonNamingPolicy.SnakeCaseUpper`, a `DateOnly` converter) rather than adding to them.
Response-shape checking matches JSON fields to properties ignoring case and names properties
through `PropertyNamingPolicy` when one is set. Throws `ArgumentNullException` when `options` is
`null`.

## Configure

Runs `mutate` immediately against the client's `GraphQLClientConfigurationBuilder`, for options with
no method of their own: `GraphQLClientOptions`, `WebsocketJsonSerializer` and
`RemoveSubscriptionsFromSchema`. Its properties are listed on
[IGraphQLClientConfiguration](/docs/sdk-reference/graphql-client/graphql-client-configuration#graphqlclientconfigurationbuilder).
Throws `ArgumentNullException` when `mutate` is `null`.

## Examples

```csharp compile
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .UseFileSchema("schema.graphql")
    .WithStrictness(ResponseStrictness.WarnOnDrift)
    .HttpClientBuilder.AddHttpMessageHandler(() => new AuthHeaderHandler());
```

```csharp compile
using System.Text.Json;
using System.Text.Json.Serialization;

var shared = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .ConfigureHttpClient(shared)
    .ConfigureJson(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    })
    .Configure(o => o.RemoveSubscriptionsFromSchema = false);
```

## See also

- [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client)
- [UseAssemblySchema](/docs/sdk-reference/graphql-client/use-assembly-schema)
- [UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation)
- [IGraphQLClientConfiguration](/docs/sdk-reference/graphql-client/graphql-client-configuration)
- [Check outbound queries against a schema snapshot](/docs/api/check-outbound-queries-against-a-schema-snapshot)
