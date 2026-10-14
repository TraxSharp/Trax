---
title: UseAssemblySchema
description: "Reference for UseAssemblySchema, which validates a client's queries against the server's HotChocolate schema built in the calling process."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 4
package: Trax.Api.GraphQL.Client.Trax
namespace: Trax.Api.GraphQL.Client.Trax
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client.Trax/TraxBuilderExtensions.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client.Trax/AssemblySchemaProvider.cs
---

# UseAssemblySchema

Replaces the client's schema provider with `AssemblySchemaProvider`, which builds the server's
HotChocolate schema in-process from the server's own configuration delegate. Declared on the static
class `TraxBuilderExtensions`.

## Signature

```csharp fragment
public static TraxGraphQLClientBuilder UseAssemblySchema(
    this TraxGraphQLClientBuilder builder,
    Action<IRequestExecutorBuilder> configureSchema)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `builder` | `TraxGraphQLClientBuilder` | Yes | The client's builder |
| `configureSchema` | `Action<IRequestExecutorBuilder>` | Yes | Configures a HotChocolate request executor the way the server's `Program.cs` does |

## Returns

The same `TraxGraphQLClientBuilder`.

## Behavior

- Throws `ArgumentNullException` when either argument is `null`.
- On the first schema request the provider runs `configureSchema` against a fresh
  `services.AddGraphQL()`, builds the executor, prints its schema to SDL and builds the validator's
  schema from that. No network call is made. A failure throws `GraphQLSchemaIntrospectionException`
  carrying the generated SDL.
- The subscription type is dropped when the client's `RemoveSubscriptionsFromSchema` is `true`, the
  default.
- The calling process takes a binary dependency on the assembly that defines `configureSchema`.
- As with the other schema methods, the last one called on a builder decides the provider.

## Example

```csharp compile
services
    .AddTraxGraphQLClient(new Uri("https://players.example.com/graphql"))
    .UseAssemblySchema(PlayerSchema.Configure)
    .UseStartupValidation(typeof(Program).Assembly);
```

## See also

- [ISchemaProvider](/docs/sdk-reference/graphql-client/i-schema-provider), which lists `AssemblySchemaProvider`'s constructors
- [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder)
- [UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation)
