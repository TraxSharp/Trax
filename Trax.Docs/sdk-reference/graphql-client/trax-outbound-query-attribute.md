---
title: TraxOutboundQueryAttribute
description: "Reference for TraxOutboundQueryAttribute, which names the external endpoint a request calls, and OutboundQueryDiscovery, which lists the marked requests."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 17
package: Trax.Api.GraphQL.Client.Trax
namespace: Trax.Api.GraphQL.Client.Trax
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client.Trax/TraxOutboundQueryAttribute.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client.Trax/OutboundQueryDiscovery.cs
---

# TraxOutboundQueryAttribute

Marks a request type as a call to a named external GraphQL endpoint. It is metadata only: it changes
no runtime behaviour and does not choose the client that validates the request.
`OutboundQueryDiscovery` reads it.

## Signature

```csharp fragment
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TraxOutboundQueryAttribute : Attribute
{
    public TraxOutboundQueryAttribute(string endpoint);
    public string Endpoint { get; }
}

public static class OutboundQueryDiscovery
{
    public static IReadOnlyList<Entry> Discover(params Assembly[] assemblies);

    public sealed record Entry(Type RequestType, string Endpoint, string? QueryName);
}
```

## Members

| Member | Description |
|--------|-------------|
| `Endpoint` | The endpoint's logical name, such as `PlayerService`. Not a URL |
| `Discover(assemblies)` | Every concrete request class in `assemblies` that carries the attribute |
| `Entry.RequestType` | The request type |
| `Entry.Endpoint` | The attribute's `Endpoint` |
| `Entry.QueryName` | The operation name read from the request's query, or `null` when the operation is anonymous, not a query or mutation, or the query cannot be read |

## Behavior

- The constructor throws `ArgumentException` for a null, empty or whitespace endpoint.
- `Discover` throws `ArgumentNullException` for `null`. It creates each type without running a
  constructor to read its `Query`; any failure there leaves `QueryName` `null`.
- Nothing in Trax calls `Discover`, and the dashboard does not show the attribute.

## Example

```csharp compile
[TraxOutboundQuery("PlayerService")]
public sealed class GetPlayerLevelRequest : IGraphQLClientRequest<int>
{
    public string Query => "query PlayerLevel { me { level } }";
}

public static class OutboundReport
{
    public static void Print()
    {
        foreach (var entry in OutboundQueryDiscovery.Discover(typeof(OutboundReport).Assembly))
            Console.WriteLine($"{entry.RequestType.Name} -> {entry.Endpoint} ({entry.QueryName})");
    }
}
```

## See also

- [GraphQLClientAttribute](/docs/sdk-reference/graphql-client/graphql-client-attribute)
- [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request)
