---
layout: default
title: IGraphQLClientRequest
description: Reference for IGraphQLClientRequest, an outbound GraphQL request typed by its response, with IGraphQLClientExecutor and the GraphQLClient attribute.
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 5
---

# IGraphQLClientRequest

An outbound GraphQL request whose result is read as `TResponse`. Implement it directly with a literal query, derive from [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request) to load the query from an embedded `.graphql` file, or derive from `TypedRequest<TResponse>` (in `Trax.Api.GraphQL.Client.Typed`) to generate it from the response type. Run it with `IGraphQLClientExecutor.Run`.

## Definition

```csharp
public interface IGenericGraphQLClientRequest
{
    string Query { get; }
    object? Variables => null;
}

public interface IGraphQLClientRequest<out TResponse> : IGenericGraphQLClientRequest
{
    JsonElement UnwrapDataElement(JsonElement data);           // default implementation
    TResponse Extract(JsonElement data, JsonSerializerOptions options); // default implementation
    bool UsesDefaultExtractor => true;
}
```

Only `Query` must be implemented. Everything else has a default.

| Member | Default | Description |
|--------|---------|-------------|
| `Query` | (required) | The GraphQL document. It must hold exactly one operation, before or after any fragments: requests are sent without an operation name, so a second operation is refused at validation. Keep it constant and pass values through `Variables`, because validation is cached per query text |
| `Variables` | `null` | The operation's variables, serialized as the `variables` object. An anonymous object or a dictionary both work |
| `UnwrapDataElement(data)` | When `data` has exactly one top-level property, returns its value; otherwise returns `data` unchanged | Navigates from the `data` envelope to the element that is deserialized. Override it to walk a nested envelope |
| `Extract(data, options)` | Calls `UnwrapDataElement`, returns `default` for a JSON `null`, and otherwise deserializes with the client's JSON options | Override it to reshape the response yourself |
| `UsesDefaultExtractor` | `true` | Set to `false` when you override `Extract`. Response-shape checking ([ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness)) runs only when this is `true`, since a custom extractor may reshape the response in ways the check cannot model |

`IGenericGraphQLClientRequest` is the non-generic view used to scan and validate requests without knowing their response type. Implement `IGraphQLClientRequest<TResponse>`, not it.

## Example

```csharp
public sealed class GetPlayerRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query => """
        query GetPlayer($id: String!) {
          player(id: $id) { id name level }
        }
        """;

    public object? Variables => new { id = Id };
}

public sealed record PlayerProfile(string Id, string Name, int Level);
```

The response `{ "data": { "player": { ... } } }` has one top-level field, so the default `UnwrapDataElement` descends into `player` and deserializes it as `PlayerProfile`.

## IGraphQLClientExecutor

```csharp
public interface IGraphQLClientExecutor
{
    Task<TReturn> Run<TReturn>(
        IGraphQLClientRequest<TReturn> request,
        CancellationToken cancellationToken = default);
}
```

Registered as a singleton by [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) (keyed, by [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client)) and safe to use concurrently. `Run`:

1. Validates `Query` against the client's schema. The first validation of a query text loads the schema if needed; later ones hit the cache.
2. Sends it as a query or a mutation, according to the operation.
3. Throws if the server returned errors or no data.
4. Checks the response shape when the client's strictness is not `Lenient` and the request uses the default extractor.
5. Returns `Extract(data, options)`.

| Exception | When |
|-----------|------|
| `GraphQLValidationException` | The query does not validate, has no operation, or has more than one. Nothing is sent. `Query` and `Errors` say why |
| `GraphQLSchemaIntrospectionException` | The schema could not be loaded for validation |
| `GraphQLExecutionException` | The server returned GraphQL errors (in `Errors`), the response had no `data`, or deserializing it as `TReturn` produced `null` (`Errors` is empty) |
| `JsonException` | The default extractor could not read the data as `TReturn`, for example a string where the type has a number. It is not wrapped |
| `GraphQLResponseShapeException` | The response shape differs from `TReturn` under `ThrowOnDrift` |
| `NotSupportedException` | The operation is a subscription |

Transport failures (a refused connection, a timeout, a non-success status the transport reports) surface as the exceptions GraphQL.Client and `HttpClient` throw.

## GraphQLClientAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLClientAttribute(object key) : Attribute
```

Names the keyed client a request belongs to. It decides which client's validation checks the request (see [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client#validating-a-keyed-clients-requests)); it does not decide where the request is sent.

## Package

```
dotnet add package Trax.Api.GraphQL.Client
```
