---
title: UseStartupValidation
description: "Reference for UseStartupValidation, a hosted service that validates a client's request types when the host starts and stops it on failure."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 5
package: Trax.Api.GraphQL.Client.Trax
namespace: Trax.Api.GraphQL.Client.Trax
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client.Trax/TraxBuilderExtensions.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client.Trax/StartupValidator.cs
---

# UseStartupValidation

Registers a hosted service that validates this client's request types in `assemblies` when the host
starts. Declared on the static class `TraxBuilderExtensions`.

## Signature

```csharp fragment
public static TraxGraphQLClientBuilder UseStartupValidation(
    this TraxGraphQLClientBuilder builder,
    params Assembly[] assemblies)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `builder` | `TraxGraphQLClientBuilder` | Yes | The client's builder |
| `assemblies` | `Assembly[]` | Yes | The assemblies whose request types are validated. At least one |

## Returns

The same `TraxGraphQLClientBuilder`.

## Behavior

- Throws `ArgumentNullException` when `builder` or `assemblies` is `null`, and `ArgumentException`
  when `assemblies` is empty.
- The hosted service runs once, in `StartAsync`, and performs the same check as
  [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies)
  for this builder's client: an unkeyed client validates the request types with no
  `[GraphQLClient]` mark, a keyed client those marked with its key.
- Any failure propagates out of `StartAsync`, so the host does not start: a query the schema rejects
  (`GraphQLValidationException`, logged at Error with the query), a schema that cannot be loaded
  (`GraphQLSchemaIntrospectionException`), no request type for this client in `assemblies`, or a
  request marked with a key no client is registered under (`InvalidOperationException`).
- Progress is logged at Information under `ILogger<GraphQLClientStartupValidator>`.
- Every validated query is cached by the client's validator, so the first `Run` of each request does
  not validate again.

## Example

```csharp compile
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .UseFileSchema("schema.graphql")
    .UseStartupValidation(typeof(Program).Assembly);
```

## See also

- [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies)
- [GraphQLClientAttribute](/docs/sdk-reference/graphql-client/graphql-client-attribute)
- [Check outbound queries against a schema snapshot](/docs/api/check-outbound-queries-against-a-schema-snapshot)
