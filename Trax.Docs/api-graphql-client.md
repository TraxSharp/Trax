---
layout: default
title: GraphQL Client
description: "Trax.Api.GraphQL.Client, the outbound GraphQL client for .NET: hand-written queries in four modes, checked against the server's schema at startup, no codegen."
nav_order: 8
section: Packages
---

# GraphQL Client

`Trax.Api.GraphQL.Client` is the outbound GraphQL story for .NET. It is the inverse of Strawberry Shake: queries are written by hand (raw string, or `.graphql` resource file), and a runtime validator confirms they match the server's schema at startup. No codegen, no `.graphql.cs` files in your repo, no IntelliSense lying to you about a schema you haven't talked to yet.

The package sits next to `Trax.Api.GraphQL` (the server). The kernel is fully usable outside Trax: any .NET app that wants to call a GraphQL endpoint can install it without dragging in trains, mediators, or a database. In-ecosystem consumers add the `.Trax` integration package (see [Trax integration](#trax-integration)) for junction support, log correlation, and `AssemblySchemaProvider`.

## Quick Setup

```bash
dotnet add package Trax.Api.GraphQL.Client
```

```csharp
services.AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"));

// Or, chain configuration:
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .WithStrictness(ResponseStrictness.ThrowOnDrift)
    .UseFileSchema("schema.graphql");

// Optional: validate the request types in your assembly against the schema.
await app.Services.ValidateGraphQLClientAssembliesAsync(typeof(Program).Assembly);
```

`AddTraxGraphQLClient` returns a `TraxGraphQLClientBuilder`. Chain `Use*Schema()` /
`WithStrictness()` / `ConfigureHttpClient()` / `UseStartupValidation()` to layer features.
If you don't chain anything, you get the kernel defaults (introspection + lenient).

## Query Modes

Three coexisting modes, all running through the same `IGraphQLClientExecutor`. Pick per request, not per app.

### Mode A: Raw string

```csharp
public sealed class GetPlayerRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query => """
        query GetPlayer($id: String!) {
          player(id: $id) { id name level }
        }
        """;

    public object Variables => new { id = Id };
}
```

Use for one-off queries, anything with fragments / unions, queries copy-pasted from a Playground session.

A request's document holds exactly one operation. Its fragments can come before or after it, since
definition order means nothing in GraphQL. A document with two operations is refused when it is
validated: the client sends no operation name, so the server could not tell which one to run.

### Mode E: `.graphql` resource file

```csharp
[GraphQLQueryResource("GetPlayer.graphql")]
public sealed class GetPlayerRequest : GraphQLResourceRequest<PlayerProfile>
{
    public required string Id { get; init; }
    public override object Variables => new { id = Id };
}
```

Place `GetPlayer.graphql` next to the C# file. In the consuming csproj:

```xml
<ItemGroup>
  <EmbeddedResource Include="**/*.graphql" />
</ItemGroup>
```

You get real GraphQL syntax highlighting from any IDE, the C# file shrinks to a POCO plus an attribute, and the loader caches the resource once per type.

### Mode D: POCO-derived queries

```csharp
[GraphQLType("Player")]
public sealed record TypedPlayer(string Id, string Name, int? Level, string Rank);

[GraphQLOperation(OperationType.Query, RootField = "player")]
public sealed class GetPlayerRequest : TypedRequest<TypedPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
```

The POCO declares the shape; `TypedQueryGenerator` walks the properties at startup and emits the query. Refactor a property, the query updates with it. Ships in `Trax.Api.GraphQL.Client.Typed`.

How a property becomes a selection:

| On the property | Selected as |
|---|---|
| nothing | the camel-cased property name: `Level` selects `level` |
| `[JsonPropertyName("rank")]` | that name |
| `[GraphQLField("name")]` | the schema field, aliased to the property's response key when they differ: `DisplayName` selects `displayName: name`, so the response deserializes without a matching `[JsonPropertyName]` |
| `[JsonIgnore]` (`Condition = Always`) | nothing: the property is never read |
| `[JsonIgnore(Condition = WhenWritingNull)]`, `WhenWritingDefault`, `Never` | the property as usual: those conditions affect writing only, and the response still fills it |

A property whose type is another POCO gets its own sub-selection. A result type that leads back
to itself, directly (`Category.Parent` is a `Category`) or through other types, is refused with
an `InvalidOperationException` naming the loop, because a GraphQL selection must be finite. Give
the nested level its own type that stops where the query should, or mark the property
`[JsonIgnore]`. The same type under two sibling properties (`Home` and `Away`) is fine.

| `[GraphQLOperation]` property | Purpose |
|---|---|
| `OperationType` (positional) | `Query` or `Mutation`. |
| `Name` | Override the operation name. Defaults to the request type name with a trailing `Request` stripped. |
| `RootField` | Override the schema field selected at the top of the operation. Defaults to the camel-cased operation name. |
| `Path` | Dot-separated chain of wrapper field names above `RootField`. Set when the schema groups fields under a multi-level envelope. |

#### Nested envelopes (`Path`)

Trax servers expose namespaced trains under `query { discover { {namespace} { {field} } } }`. To query through that envelope from a typed request, set `Path`:

```csharp
[GraphQLOperation(OperationType.Query, Path = "discover.netsuite", RootField = "typedCustomer")]
public sealed class FindCustomerByEmailRequest : TypedRequest<TypedCustomer?>
{
    [GraphQLArgument("String!", VariableName = "email")]
    public required string Email { get; init; }
}
```

Generates:

```graphql
query FindCustomerByEmail($email: String!) {
  discover {
    netsuite {
      typedCustomer(email: $email) {
        id
        email
        ...
      }
    }
  }
}
```

The default extractor walks the same path before deserializing, so consumers stay in typed mode for nested schemas instead of falling back to raw-string with a custom `Extract`. Path values must be dot-separated field names with no whitespace; empty strings, leading/trailing dots, and doubled dots throw at startup.

## Schema Providers

| Provider | When to use |
|---|---|
| `IntrospectingSchemaProvider` (default) | The live endpoint is reachable and lets this client introspect. Cheapest setup, weakest guarantee (drift between intro and check). A Trax server allows introspection only in Development unless its host passes `AllowIntrospection` a predicate that admits this client, so against a production Trax server pick one of the other two. |
| `FileSchemaProvider` | CI, air-gapped builds, or you want startup validation against a checked-in SDL snapshot. Use a periodic introspection job to keep the snapshot fresh and alert on drift separately. |
| `AssemblySchemaProvider` | In-ecosystem only. Builds the server's `ISchema` in-process from its DLL. Strongest query-string guarantee with zero network and zero file drift. Ships in `Trax.Api.GraphQL.Client.Trax`. |

Every provider loads the schema on first use and shares it after that. A load that fails (the
server was down, the file was missing) is not kept: the next validation loads again, so one bad
moment at boot does not leave the client unable to validate until the process restarts. A
caller's cancellation token stops that caller waiting; a load other callers are waiting on
carries on.

```csharp
// Pick a schema provider on the builder. Default is introspection; calling Use*Schema
// swaps in the provider you want.
services.AddTraxGraphQLClient(uri).UseFileSchema("schema.graphql");
services.AddTraxGraphQLClient(uri).UseAssemblySchema(PlayerSchema.Configure);  // .Trax package
```

## Talking to multiple servers

One `AddTraxGraphQLClient` call registers a single client. To call two servers with different
schemas from the same container, register each under a key with `AddKeyedTraxGraphQLClient` and
resolve by key:

```csharp
services.AddKeyedTraxGraphQLClient("serverB", new Uri("https://b.example.com/graphql"))
        .UseFileSchema("b.graphql");
services.AddKeyedTraxGraphQLClient("serverC", new Uri("https://c.example.com/graphql"))
        .UseFileSchema("c.graphql");
```

```csharp
public class CrossServerTrain(
    [FromKeyedServices("serverB")] IGraphQLClientExecutor b,
    [FromKeyedServices("serverC")] IGraphQLClientExecutor c)
{
    // b talks to server B, c talks to server C.
}
```

The key names the downstream server. It is arbitrary (the library never inspects it) and
independent of the server-side `Namespace` that shows up in a request's `Path`. Each key gets
its own configuration, `HttpClient`, schema provider, and validator cache, so a request meant
for one server fails schema validation if you run it through the other server's key.

Without keying, a second `AddTraxGraphQLClient` call overrides the first (last registration
wins) and both clients validate against a single schema. Keying keeps them isolated. Every
builder method works on a keyed registration (`UseFileSchema`, `UseAssemblySchema`,
`WithStrictness`, `ConfigureHttpClient`, `UseStartupValidation`).

To validate up front, mark each request with the key of the client it belongs to:

```csharp
[GraphQLClient("serverB")]
public sealed class GetInvoiceRequest : IGraphQLClientRequest<Invoice> { /* ... */ }

[GraphQLClient("serverC")]
public sealed class GetShipmentRequest : IGraphQLClientRequest<Shipment> { /* ... */ }
```

Validation for a keyed client checks only the requests marked with its key, and validation for
the unkeyed client checks only the requests with no mark, so requests for every server can share
one assembly. The key is compared by value, so an enum key works as well as a string. The mark
only steers validation: which server a request goes to is still decided by the executor you
resolve.

```csharp
services.AddKeyedTraxGraphQLClient("serverB", serverBUri).UseStartupValidation(typeof(Program).Assembly);
services.AddKeyedTraxGraphQLClient("serverC", serverCUri).UseStartupValidation(typeof(Program).Assembly);

// Or without the .Trax package:
await app.Services.ValidateGraphQLClientAssembliesAsync("serverB", typeof(Program).Assembly);
```

A validation that finds no request for its client refuses to start, naming the key. That is a
request someone forgot to mark, and passing after checking nothing would hide it. A request
marked with a key no client is registered under is validated by nobody, so check the spelling of
the key.

The single-server `AddTraxGraphQLClient` is unchanged. Use it when you talk to one server.

## Response Strictness

Strict-extract catches "I added a field to the query but forgot to add it to the POCO" on the first response, not the hundredth bug report. Validation runs once per request type (cached) and only when the request uses the default `Extract`.

```csharp
services.AddTraxGraphQLClient(uri).WithStrictness(ResponseStrictness.ThrowOnDrift);
```

| Setting | Behavior |
|---|---|
| `Lenient` (default) | Extra JSON fields silently ignored. Matches System.Text.Json's default. |
| `WarnOnDrift` | Drift logged at warning level via `ILogger<GraphQLClientExecutor>`. Call still succeeds. Best for production. |
| `ThrowOnDrift` | Drift throws `GraphQLResponseShapeException`. Best for dev and integration tests. |

## Trax Integration

The `Trax.Api.GraphQL.Client.Trax` package contributes additional methods on the
`TraxGraphQLClientBuilder` returned by `AddTraxGraphQLClient`:

```csharp
services
    .AddTraxGraphQLClient(playerServiceUri)
    .UseAssemblySchema(PlayerSchema.Configure)          // strongest schema source
    .UseStartupValidation(typeof(Program).Assembly);    // boot fails on drift
```

Available methods (extension methods on `TraxGraphQLClientBuilder`):

| Method | What it does |
|---|---|
| `.UseAssemblySchema(configureDelegate)` | Builds the server's HotChocolate schema in-process from the same delegate the server uses. Zero network, zero file drift. |
| `.UseStartupValidation(assemblies)` | Registers a hosted service that validates this client's request types at boot: those marked `[GraphQLClient(key)]` with its key, or the unmarked ones for the unkeyed client. Schema drift becomes a startup failure, not a runtime 400, and finding no request for the client fails too. |

External consumers install only `Trax.Api.GraphQL.Client` and never see these methods.

## SDK Reference

> [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) | [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client) | [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/builder) | [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-assemblies) | [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request) | [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request) | [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness)
