---
layout: default
title: GraphQLResourceRequest
description: Reference for GraphQLResourceRequest, the base class for a request whose query lives in an embedded .graphql file, and how the resource is found.
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 6
---

# GraphQLResourceRequest

Base class for a request whose query lives in a `.graphql` file compiled into the assembly as an embedded resource. The C# class shrinks to the response type, the variables and an attribute, and the query gets GraphQL syntax highlighting in any editor.

## Definition

```csharp
public abstract class GraphQLResourceRequest<TResponse> : IGraphQLClientRequest<TResponse>
{
    public virtual string Query { get; }       // loaded from the embedded resource
    public virtual object? Variables => null;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLQueryResourceAttribute(string resourceName) : Attribute
{
    public string ResourceName { get; }
}
```

| Member | Description |
|--------|-------------|
| `Query` | The text of the resource named by `[GraphQLQueryResource]`. Loaded on first access and cached per request type, never in the constructor, so assembly validation can read it from an uninitialized instance |
| `Variables` | `null` unless overridden |
| `[GraphQLQueryResource(resourceName)]` | Names the resource file, for example `GetPlayer.graphql`, relative to the request type's namespace |

Everything else (`UnwrapDataElement`, `Extract`) comes from [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request).

## Example

```csharp
[GraphQLQueryResource("GetPlayer.graphql")]
public sealed class GetPlayerRequest : GraphQLResourceRequest<PlayerProfile>
{
    public required string Id { get; init; }
    public override object? Variables => new { id = Id };
}
```

`GetPlayer.graphql`, next to the C# file:

```graphql
query GetPlayer($id: String!) {
  player(id: $id) { id name level }
}
```

The consuming project embeds the files:

```xml
<ItemGroup>
  <EmbeddedResource Include="**/*.graphql" />
</ItemGroup>
```

## How the resource is found

For a request type in namespace `Game.Clients` in assembly `Game.Api`, with `[GraphQLQueryResource("Queries/GetPlayer.graphql")]` (`/` and `\` read as `.`), these names are tried in order:

1. `Game.Clients.Queries.GetPlayer.graphql`, the request type's namespace plus the name
2. `Game.Api.Queries.GetPlayer.graphql`, the assembly name plus the name
3. `Queries.GetPlayer.graphql`, the name as given
4. any resource in the assembly whose name ends with one of those, or with the name as written, ignoring case

The SDK names an embedded resource after the project's root namespace and the file's folder, so the first two match when the namespace follows the folder layout, and the last catches the rest. Only the request type's own assembly is searched.

## Exceptions

`Query` throws `InvalidOperationException` when:

- the request type has no `[GraphQLQueryResource]`
- no resource matches. The message lists the names tried and every resource the assembly holds, which is usually enough to spot a missing `EmbeddedResource` item or a renamed folder.

Validation reads `Query`, so with [startup validation](/docs/sdk-reference/graphql-client/builder#usestartupvalidation) either one stops the host from starting rather than failing the first call.

**`GraphQLQueryResourceAttribute`** throws `ArgumentException` when `resourceName` is null, empty or whitespace.

## Package

```
dotnet add package Trax.Api.GraphQL.Client
```
