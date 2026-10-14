---
title: Typed requests
description: "Reference for TypedRequest and its attributes in Trax.Api.GraphQL.Client.Typed: how a GraphQL query is generated from a request and a response POCO."
type: reference
area: api
nav_order: 3
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client.Typed
---

# Typed requests

`TypedRequest<TResponse>`, in `Trax.Api.GraphQL.Client.Typed`, generates a request's query from
the request type and the response type instead of taking it as text. The query is generated on
first access of `Query`, cached per request type, and validated against the schema like any other
request.

```csharp compile
using Trax.Api.GraphQL.Client.Typed;

[GraphQLType("Player")]
public sealed record TypedPlayer(string Id, string Name, int? Level, string Rank);

[GraphQLOperation(OperationType.Query, RootField = "player")]
public sealed class GetPlayerRequest : TypedRequest<TypedPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
```

generates

```graphql
query GetPlayer($id: String!) {
  player(id: $id) {
    id
    name
    level
    rank
  }
}
```

## TypedRequest&lt;TResponse&gt;

| Member | Description |
|---|---|
| `Query` | The generated document. Throws `InvalidOperationException` when the request has no `[GraphQLOperation]`, the response type (or its element type, for a list) has no `[GraphQLType]`, or the response type refers back to itself. |
| `Variables` | A dictionary of each `[GraphQLArgument]` property's current value, keyed by variable name; `null` when the request declares no arguments. |
| `UnwrapDataElement(data)` | Walks the `data` envelope through each `Path` segment, then into the root field. A missing field or a non-object on the way throws `GraphQLExecutionException` naming the field, the request type and what was found. |

## Attributes

| Attribute | On | Arguments | Effect |
|---|---|---|---|
| `[GraphQLOperation(OperationType)]` | the request class | `OperationType` (`Query` or `Mutation`), `Name`, `RootField`, `Path` | Marks the class as a typed request. Required. |
| `[GraphQLType(typeName)]` | a response class | the schema type's name | Required on the response type or its list element type. Marks a nested class as an object type. `typeName` is not read when generating; field presence is checked at validation. |
| `[GraphQLArgument(graphQLType)]` | a request property | the full GraphQL type, such as `String!`; `VariableName` | Declares `$name: Type` on the operation and passes it to the root field as the argument of the same name. `VariableName` defaults to the camel-cased property name. |
| `[GraphQLField(fieldName)]` | a response property | the schema field name | Selects that field, aliased to the property's response key when they differ. |

### GraphQLOperation properties

| Property | Default | Description |
|---|---|---|
| `OperationType` | (constructor) | `Query` or `Mutation`. Subscriptions are not supported. |
| `Name` | the request type name, without a trailing `Request` | The operation name. |
| `RootField` | the camel-cased operation name | The field selected on the root type. |
| `Path` | none | Dot-separated wrapper fields above `RootField`, such as `discover.netsuite`. An empty string, an empty segment (a leading, trailing or doubled dot) or whitespace throws `InvalidOperationException` when the query is generated. |

A Trax server groups a namespaced train's field under `discover { <namespace> { ... } }`, so a
typed request reaches it with `Path = "discover.<namespace>"`:

```csharp compile
using Trax.Api.GraphQL.Client.Typed;

[GraphQLType("Customer")]
public sealed record TypedCustomer(string Id, string Email);

[GraphQLOperation(OperationType.Query, Path = "discover.netsuite", RootField = "typedCustomer")]
public sealed class FindCustomerByEmailRequest : TypedRequest<TypedCustomer?>
{
    [GraphQLArgument("String!", VariableName = "email")]
    public required string Email { get; init; }
}
```

## How a property is selected

| On the response property | Selected as |
|---|---|
| nothing | the property name camel-cased as System.Text.Json and HotChocolate do it: `Level` selects `level`, `ID` selects `id`, `URLPath` selects `urlPath` |
| `[JsonPropertyName("rank")]` | that name |
| `[GraphQLField("name")]` | the schema field, aliased to the response key when they differ: `DisplayName` selects `displayName: name`. A response key that is not a valid GraphQL name cannot be an alias, and generation throws |
| `[JsonIgnore]` (`Condition = Always`) | nothing |
| `[JsonIgnore(Condition = WhenWritingNull)]`, `WhenWritingDefault`, `Never` | the property as usual; those conditions affect writing only |
| an indexer | nothing |

| Property type | Selected as |
|---|---|
| a class marked `[GraphQLType]` | an object, with its own sub-selection |
| a class with public properties, outside the `System` namespaces | an object, with its own sub-selection |
| a string, enum, struct, primitive, or a class in a `System` namespace (`Uri`, `Version`, `JsonObject`) | a scalar |
| an array or `IEnumerable<T>` | as its element type |

The same naming applies to arguments, so `URLPath` binds `$urlPath`.

## Refusals

| Case | Result |
|---|---|
| A response type that leads back to itself, directly (`Category.Parent` is a `Category`) or through other types | `InvalidOperationException` naming the loop. The same type under two sibling properties is not a loop. |
| An open generic request type (`PagedRequest<T>`) found when an assembly is validated | Skipped; the closed request types deriving from it are validated. |

## See also

- [Call another GraphQL server](/docs/api-graphql-client)
- [How the GraphQL client checks queries](/docs/api/graphql-client-validation)
- [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request)

## SDK Reference

> [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request) | [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies)
