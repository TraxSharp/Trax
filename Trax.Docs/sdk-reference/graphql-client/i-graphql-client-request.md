---
title: IGraphQLClientRequest
description: "Reference for IGraphQLClientRequest and IGenericGraphQLClientRequest, an outbound GraphQL request typed by its response, and their default members."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 7
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Interfaces/IGraphQLClientRequest.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Interfaces/IGenericGraphQLClientRequest.cs
---

# IGraphQLClientRequest

An outbound GraphQL request whose result is read as `TResponse`, run with
[IGraphQLClientExecutor.Run](/docs/sdk-reference/graphql-client/i-graphql-client-executor). Three
implementations exist: implement the interface with a literal query, derive from
[GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request) to load the
query from an embedded `.graphql` file, or derive from
[TypedRequest](/docs/sdk-reference/graphql-client/typed-request) to generate it from the response
type.

## Signature

```csharp fragment
public interface IGenericGraphQLClientRequest
{
    string Query { get; }
    object? Variables => null;
}

public interface IGraphQLClientRequest<out TResponse> : IGenericGraphQLClientRequest
{
    JsonElement UnwrapDataElement(JsonElement data);                  // default implementation
    TResponse Extract(JsonElement data, JsonSerializerOptions options); // default implementation
    bool UsesDefaultExtractor => true;
}
```

## Members

| Member | Default | Description |
|--------|---------|-------------|
| `Query` | (required) | The GraphQL document. It holds exactly one operation, before or after any fragments: requests are sent without an operation name, so a second operation is refused at validation. Validation is cached per query text |
| `Variables` | `null` | The operation's variables, serialized as the `variables` object. An anonymous object or a dictionary |
| `UnwrapDataElement(data)` | When `data` is an object with exactly one property, returns that property's value; otherwise returns `data` | Navigates from the `data` envelope to the element that is deserialized |
| `Extract(data, options)` | Calls `UnwrapDataElement`; returns `default` for a JSON `null`; otherwise deserializes with `options`, throwing `GraphQLExecutionException` when that yields `null` | Produces the result |
| `UsesDefaultExtractor` | `true` | Whether `Extract` is the default. Response-shape checking ([ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness)) runs only when it is `true` |

`IGenericGraphQLClientRequest` is the non-generic view the validator scans; a request implements
`IGraphQLClientRequest<TResponse>`.

## Example

```csharp compile
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

The response `{ "data": { "player": { ... } } }` has one top-level field, so the default
`UnwrapDataElement` returns `player` and `Extract` deserializes it as `PlayerProfile`.

## See also

- [IGraphQLClientExecutor](/docs/sdk-reference/graphql-client/i-graphql-client-executor)
- [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request)
- [TypedRequest](/docs/sdk-reference/graphql-client/typed-request)
- [GraphQLClientAttribute](/docs/sdk-reference/graphql-client/graphql-client-attribute)
