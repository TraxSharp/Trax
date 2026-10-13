---
title: ResponseStrictness
description: "Reference for ResponseStrictness, which decides how the client treats response fields that differ from the response type's properties."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 15
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Configuration/ResponseStrictness.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services/GraphQLClientExecutor/ResponseShapeValidator.cs
---

# ResponseStrictness

How the executor treats a response whose fields differ from the response type's properties: a property the query does not select, or a selected field no property reads.

## Signature

```csharp fragment
public enum ResponseStrictness
{
    Lenient,
    WarnOnDrift,
    ThrowOnDrift,
}
```

## Members

| Value | Behavior |
|-------|----------|
| `Lenient` (default) | No check. Extra JSON fields are ignored and missing ones leave the property at its default, as System.Text.Json does on its own |
| `WarnOnDrift` | Drift is logged at `Warning` under `ILogger<GraphQLClientExecutor>`, naming the type and the extra and missing fields. The call still succeeds |
| `ThrowOnDrift` | Drift throws `GraphQLResponseShapeException` |

## Behavior

After the server answers, the element the request's `UnwrapDataElement` returns is compared with the public instance properties of the response type:

- **Extra**: a JSON field with no matching property.
- **Missing**: a property with no matching JSON field.

A property's expected name is its `[JsonPropertyName]` if it has one, otherwise its name passed through the JSON options' `PropertyNamingPolicy`, otherwise its name. Names are compared ignoring case, so `id` matches `Id`. A property marked `[JsonIgnore]` with `Condition = Always` is not expected; other `JsonIgnore` conditions affect writing only, so those properties still are.

The check:

- covers one level. Properties of nested objects are not compared.
- applies only when the unwrapped element is a JSON object. A scalar, a list or `null` is not checked.
- runs only for requests that use the default extractor (`UsesDefaultExtractor` is `true`). A request that overrides `Extract` is never checked.
- is cached per response type and set of JSON field names, so a response shape seen before costs a dictionary lookup.

`GraphQLResponseShapeException` carries `TargetType`, `ExtraJsonFields` and `MissingJsonFields`;
see [Client exceptions](/docs/sdk-reference/graphql-client/graphql-client-exceptions#graphqlresponseshapeexception).

## Example

```csharp compile
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .WithStrictness(ResponseStrictness.ThrowOnDrift);
```

## See also

- [TraxGraphQLClientBuilder.WithStrictness](/docs/sdk-reference/graphql-client/trax-graphql-client-builder#withstrictness)
- [IGraphQLClientExecutor](/docs/sdk-reference/graphql-client/i-graphql-client-executor)
