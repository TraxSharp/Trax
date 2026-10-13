---
title: GraphQLClientAttribute
description: "Reference for GraphQLClientAttribute, which marks the keyed GraphQL client a request type belongs to for validation."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 14
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Interfaces/GraphQLClientAttribute.cs
---

# GraphQLClientAttribute

Names the keyed client a request type belongs to, by the key passed to
[AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client).

## Signature

```csharp fragment
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLClientAttribute : Attribute
{
    public GraphQLClientAttribute(object key);
    public object Key { get; }
}
```

## Properties

| Property | Description |
|----------|-------------|
| `Key` | The service key of the client the request belongs to, compared with `Equals`: a string or an enum value matching the one the client was registered with |

## Behavior

- The constructor throws `ArgumentNullException` for a `null` key.
- Validation for a keyed client ([UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation)
  on its builder, or the keyed overload of
  [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies))
  checks only the request types marked with its key. The unkeyed client's validation checks only the
  unmarked types.
- A request marked with a key no client is registered under fails both validations with
  `InvalidOperationException`.
- The mark is read from the request type itself, not inherited.
- Running a request does not read it: the executor the request is run through decides which server
  it goes to.

## Example

```csharp compile
[GraphQLClient("billing")]
public sealed class GetInvoiceTotalRequest : IGraphQLClientRequest<decimal>
{
    public required string Id { get; init; }

    public string Query => "query InvoiceTotal($id: ID!) { invoice(id: $id) { total } }";

    public object? Variables => new { id = Id };
}
```

## See also

- [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client)
- [TraxOutboundQueryAttribute](/docs/sdk-reference/graphql-client/trax-outbound-query-attribute), which is metadata only
