---
title: TypedRequest
description: "Reference for TypedRequest and the GraphQLOperation, GraphQLType, GraphQLArgument and GraphQLField attributes that generate a query from C# types."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 9
package: Trax.Api.GraphQL.Client.Typed
namespace: Trax.Api.GraphQL.Client.Typed
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client.Typed/TypedRequest.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client.Typed/Attributes.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client.Typed/TypedQueryGenerator.cs
---

# TypedRequest

Base class for a request whose GraphQL document is generated from the request type and its result
type. The request carries `[GraphQLOperation]`, its variables are properties marked
`[GraphQLArgument]`, and the result type carries `[GraphQLType]`. The generated query is validated
and sent like any other [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request).

## Signature

```csharp fragment
public abstract class TypedRequest<TResponse> : IGraphQLClientRequest<TResponse>
{
    public virtual string Query { get; }
    public virtual object? Variables { get; }
    public virtual JsonElement UnwrapDataElement(JsonElement data);
}

public sealed class GraphQLOperationAttribute : Attribute
{
    public GraphQLOperationAttribute(OperationType operationType);
    public OperationType OperationType { get; }
    public string? Name { get; init; }
    public string? RootField { get; init; }
    public string? Path { get; init; }
}

public enum OperationType { Query, Mutation }

public sealed class GraphQLTypeAttribute : Attribute
{
    public GraphQLTypeAttribute(string typeName);
    public string TypeName { get; }
}

public sealed class GraphQLArgumentAttribute : Attribute
{
    public GraphQLArgumentAttribute(string graphQLType);
    public string GraphQLType { get; }
    public string? VariableName { get; init; }
}

public sealed class GraphQLFieldAttribute : Attribute
{
    public GraphQLFieldAttribute(string fieldName);
    public string FieldName { get; }
}
```

## Members

| Member | Description |
|--------|-------------|
| `Query` | The generated document, built on first access and cached per request type |
| `Variables` | A dictionary of every `[GraphQLArgument]` property's current value, keyed by variable name; `null` when there are none |
| `UnwrapDataElement(data)` | Descends through the `Path` wrapper fields, then into the root field. A missing field or a non-object on the way throws `GraphQLExecutionException` naming the field, the request type and the JSON kind found |
| `[GraphQLOperation(operationType)]` | On the request class. `OperationType` is `Query` or `Mutation`. `Name` is the operation name, default the class name without a trailing `Request`. `RootField` is the schema field, default the camel-cased operation name. `Path` is a dot-separated chain of wrapper fields above the root field, such as `"discover.players"` |
| `[GraphQLType(typeName)]` | On the result class (or a list's element class), and on any nested class that gets its own selection. `TypeName` is not read by the generator; field presence is checked when the query is validated. Inherited |
| `[GraphQLArgument(graphQLType)]` | On a request property. Declares `$name: graphQLType` and passes it to the root field as the argument of the same name. `VariableName` overrides the camel-cased property name |
| `[GraphQLField(fieldName)]` | On a result property. Selects `fieldName`, aliased to the property's response key when they differ (`displayName: name`) |

## Behavior

- The selection is every public instance property of the result type except indexers and those
  marked `[JsonIgnore(Condition = JsonIgnoreCondition.Always)]`. A property's response key is its
  `[JsonPropertyName]`, else its camel-cased name.
- A property whose type carries `[GraphQLType]`, or is a non-BCL class with properties, gets a nested
  selection; a list property is treated as its element type. Strings, primitives, enums, structs and
  `System.*` classes are leaves.
- Generation throws `InvalidOperationException` when the request has no `[GraphQLOperation]`, the
  result type has no `[GraphQLType]`, `Path` is empty or has an empty or whitespace segment, a
  result type refers back to itself through its properties, or a `[GraphQLField]` alias would be a
  `[JsonPropertyName]` that is not a valid GraphQL name.
- Subscriptions are not generated. Fragments, unions, interfaces and directives are not generated.
- The same types always produce the same query text.

## Example

```csharp compile
[GraphQLOperation(OperationType.Query, RootField = "player")]
public sealed class GetPlayerRequest : TypedRequest<PlayerProfile>
{
    [GraphQLArgument("String!")]
    public required string Id { get; init; }
}

[GraphQLType("Player")]
public sealed class PlayerProfile
{
    public string Id { get; init; } = "";

    [GraphQLField("name")]
    public string DisplayName { get; init; } = "";

    public int Level { get; init; }
}
```

The generated document:

```graphql
query GetPlayer($id: String!) {
  player(id: $id) {
    id
    displayName: name
    level
  }
}
```

## See also

- [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request)
- [IGraphQLClientExecutor](/docs/sdk-reference/graphql-client/i-graphql-client-executor)
- [GraphQL Client](/docs/api-graphql-client)
