---
layout: default
title: TraxGraphQLClientBuilder
description: "Reference for TraxGraphQLClientBuilder: schema sources, response strictness, JSON and HttpClient configuration, and startup validation."
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 3
---

# TraxGraphQLClientBuilder

The fluent builder returned by [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) and [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client). Each method changes the registration in place and returns the builder, so the chain can stop anywhere and the registration is consistent with the calls made so far. On a keyed builder every method applies to that key only.

```csharp
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .UseFileSchema("schema.graphql")
    .WithStrictness(ResponseStrictness.WarnOnDrift)
    .ConfigureHttpClient(httpClient)
    .DisposeHttpClient();
```

## Methods

| Method | Package | Description |
|--------|---------|-------------|
| [`UseIntrospection()`](#useintrospection) | Client | Validate against the schema the endpoint reports by introspection. The default |
| [`UseFileSchema(string sdlPath)`](#usefileschema) | Client | Validate against a checked-in SDL file |
| [`UseAssemblySchema(Action<IRequestExecutorBuilder>)`](#useassemblyschema) | Client.Trax | Build the server's HotChocolate schema in-process |
| [`WithStrictness(ResponseStrictness)`](#withstrictness) | Client | How strictly responses are checked |
| [`ConfigureHttpClient(HttpClient)`](#configurehttpclient) | Client | Supply the `HttpClient` requests go through |
| [`DisposeHttpClient(bool dispose = true)`](#disposehttpclient) | Client | Dispose that `HttpClient` with the client |
| [`ConfigureJson(JsonSerializerOptions)`](#configurejson) | Client | Replace the options responses are deserialized with |
| [`Configure(Action<GraphQLClientConfigurationBuilder>)`](#configure) | Client | Set any option directly |
| [`UseStartupValidation(params Assembly[])`](#usestartupvalidation) | Client.Trax | Validate this client's requests when the host starts |

The `Services` property exposes the `IServiceCollection` the client was registered against.

## Schema source

The three `Use*` schema methods replace one another: the last one called decides where the schema comes from. Every provider loads the schema on first use and shares it after that. A load that fails is not kept, so the next validation tries again.

### UseIntrospection

```csharp
public TraxGraphQLClientBuilder UseIntrospection()
```

Validates against the schema the endpoint returns for a standard introspection query. This is what you get when no schema method is called; call it to say so explicitly, or to switch back after another `Use*` call.

The subscription root is dropped from the introspected schema by default (see `RemoveSubscriptionsFromSchema` under [Configure](#configure)). Custom scalars the server declares are accepted as any value.

A Trax server answers introspection only in Development unless its host allows the client with `AllowIntrospection`. Against a production Trax server use `UseFileSchema` or `UseAssemblySchema`. A failed introspection throws `GraphQLSchemaIntrospectionException` from the validation that triggered it.

### UseFileSchema

```csharp
public TraxGraphQLClientBuilder UseFileSchema(string sdlPath)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `sdlPath` | `string` | Yes | Absolute path, or one relative to the process's working directory, of an SDL file such as `schema.graphql` |

Validates against a checked-in schema snapshot. Use it when the endpoint is unreachable at startup (CI, air-gapped builds) or when you want validation against a known schema rather than whatever the endpoint returns today. The file is read on first use, not at registration. A missing, empty or invalid file throws `GraphQLSchemaIntrospectionException` from that validation.

**Throws**: `ArgumentException` when `sdlPath` is null, empty or whitespace.

Keep the snapshot fresh with a separate job that introspects the server and alerts on a difference, so "queries validate" and "the snapshot is current" are two signals.

### UseAssemblySchema

```csharp
public static TraxGraphQLClientBuilder UseAssemblySchema(
    this TraxGraphQLClientBuilder builder,
    Action<IRequestExecutorBuilder> configureSchema)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configureSchema` | `Action<IRequestExecutorBuilder>` | Yes | The same HotChocolate configuration delegate the server's `Program.cs` uses |

Builds the server's schema in-process from the server's own configuration, so there is no network call and no file to drift. The calling process takes a binary dependency on the assembly that defines `configureSchema`. In `Trax.Api.GraphQL.Client.Trax`.

## Responses

### WithStrictness

```csharp
public TraxGraphQLClientBuilder WithStrictness(ResponseStrictness strictness)
```

Sets how the executor treats a response whose fields differ from the response type's properties. Default `Lenient`. See [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness).

### ConfigureJson

```csharp
public TraxGraphQLClientBuilder ConfigureJson(JsonSerializerOptions options)
```

Replaces the `JsonSerializerOptions` the executor deserializes responses with. The defaults are:

- `PropertyNameCaseInsensitive = true`
- a `JsonStringEnumConverter` with `JsonNamingPolicy.SnakeCaseUpper`, so a GraphQL enum value `GOLD_TIER` reads into `GoldTier`
- a `DateOnly` converter

The supplied options replace these rather than adding to them. Response-shape checking matches JSON fields to properties ignoring case, and names properties through `PropertyNamingPolicy` when one is set, so keep case-insensitive matching and add your own enum converter when you replace them.

**Throws**: `ArgumentNullException` when `options` is `null`.

```csharp
services.AddTraxGraphQLClient(uri).ConfigureJson(new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
});
```

## HTTP

### ConfigureHttpClient

```csharp
public TraxGraphQLClientBuilder ConfigureHttpClient(HttpClient httpClient)
```

Supplies the `HttpClient` that requests and introspection go through. Use it to attach authentication handlers, logging delegates or timeouts. The client's `BaseAddress` is overwritten with the URI the client was registered with.

By default the library creates its own `HttpClient`. A supplied one is not disposed by the library unless you call `DisposeHttpClient()`.

**Throws**: `ArgumentNullException` when `httpClient` is `null`.

```csharp
var handler = new AuthHeaderHandler(tokens) { InnerHandler = new HttpClientHandler() };

services.AddTraxGraphQLClient(uri)
        .ConfigureHttpClient(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) })
        .DisposeHttpClient();
```

### DisposeHttpClient

```csharp
public TraxGraphQLClientBuilder DisposeHttpClient(bool dispose = true)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `dispose` | `bool` | No | `true` (the default when called) to dispose the `HttpClient` with the client; `false` to leave it to you |

Without this call the caller owns the `HttpClient`, and only the GraphQL.Client wrapper around it is disposed when the container disposes the client's configuration. Call it when the `HttpClient` you passed to `ConfigureHttpClient` belongs to this client alone. Leave it off when the `HttpClient` is shared or comes from `IHttpClientFactory`.

## Escape hatch

### Configure

```csharp
public TraxGraphQLClientBuilder Configure(Action<GraphQLClientConfigurationBuilder> mutate)
```

Runs `mutate` immediately against the underlying `GraphQLClientConfigurationBuilder`, for options without a dedicated method. Its settable properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `HttpClient` | `HttpClient` | a new `HttpClient` | Same as `ConfigureHttpClient` |
| `DisposeHttpClient` | `bool` | `false` | Same as `DisposeHttpClient()` |
| `JsonSerializerOptions` | `JsonSerializerOptions` | see [ConfigureJson](#configurejson) | Same as `ConfigureJson` |
| `ResponseStrictness` | `ResponseStrictness` | `Lenient` | Same as `WithStrictness` |
| `GraphQLClientOptions` | `GraphQLHttpClientOptions` | `new()` | GraphQL.Client's own transport options |
| `WebsocketJsonSerializer` | `IGraphQLWebsocketJsonSerializer` | `SystemTextJsonSerializer` | The serializer GraphQL.Client uses for request and response envelopes |
| `RemoveSubscriptionsFromSchema` | `bool` | `true` | Drop the subscription root from an introspected schema. Applies to `UseIntrospection` only |

**Throws**: `ArgumentNullException` when `mutate` is `null`.

## Startup validation

### UseStartupValidation

```csharp
public static TraxGraphQLClientBuilder UseStartupValidation(
    this TraxGraphQLClientBuilder builder,
    params Assembly[] assemblies)
```

Registers a hosted service that validates this client's request types in `assemblies` when the host starts. A query the schema rejects throws `GraphQLValidationException`, logged with the query, and the host does not start. In `Trax.Api.GraphQL.Client.Trax`.

An unkeyed client validates the request types with no `[GraphQLClient]` mark; a keyed client validates those marked with its key. Finding no request type for the client also stops startup, because that is a request someone forgot to mark rather than nothing to check.

**Throws**: `ArgumentNullException` when `assemblies` is `null`; `ArgumentException` when it is empty.

Without the `.Trax` package, call [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-assemblies) after the app is built.

## Packages

```
dotnet add package Trax.Api.GraphQL.Client
dotnet add package Trax.Api.GraphQL.Client.Trax   # UseAssemblySchema, UseStartupValidation
```
