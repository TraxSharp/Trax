---
title: ValidateGraphQLClientAssembliesAsync
description: "Reference for ValidateGraphQLClientAssembliesAsync, which checks every request type a client owns against its schema after the app is built."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 6
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Extensions/ServiceExtensions.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Extensions/GraphQLClientValidatorExtensions.cs
redirect_from:
  - /docs/sdk-reference/graphql-client/validate-assemblies
---

# ValidateGraphQLClientAssembliesAsync

Validates every request type a client owns in a set of assemblies against that client's schema.
Called on the built container. Declared on the static class `ServiceExtensions`.

## Signatures

```csharp fragment
public static Task ValidateGraphQLClientAssembliesAsync(
    this IServiceProvider services,
    params Assembly[] assemblies)

public static Task ValidateGraphQLClientAssembliesAsync(
    this IServiceProvider services,
    object serviceKey,
    params Assembly[] assemblies)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceProvider` | Yes | The built container, usually `app.Services` |
| `serviceKey` | `object` | Keyed overload | The key the client was registered with by [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client) |
| `assemblies` | `Assembly[]` | Yes | The assemblies to scan |

## Returns

A `Task` that completes when every request has validated.

## Behavior

- The scan covers every concrete, closed class in `assemblies` that implements
  `IGenericGraphQLClientRequest`. The unkeyed overload validates the types with no
  [`[GraphQLClient]`](/docs/sdk-reference/graphql-client/graphql-client-attribute) mark against the
  unkeyed client's schema; the keyed overload validates the types marked
  `[GraphQLClient(serviceKey)]` against the keyed client's schema.
- Each type is created with `RuntimeHelpers.GetUninitializedObject`, without running a constructor,
  and its `Query` is validated, so a `Query` must not depend on constructor state. A literal string,
  a [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request) and a
  [TypedRequest](/docs/sdk-reference/graphql-client/typed-request) qualify.
- Validation stops at the first failure. Each query that passes is cached by the validator, which is
  the cache the executor uses.

| Exception | When |
|-----------|------|
| `ArgumentNullException` | An argument is `null` |
| `GraphQLValidationException` | A query does not validate, has no operation, or has more than one. `Query` holds the failing text and `Errors` the validator's errors |
| `GraphQLSchemaIntrospectionException` | The schema could not be loaded |
| `InvalidOperationException` | No request type in `assemblies` belongs to this client; a request in `assemblies` is marked with a key no client is registered under; or (keyed overload) no client is registered under `serviceKey` |

## Example

```csharp compile
var app = builder.Build();

await app.Services.ValidateGraphQLClientAssembliesAsync(typeof(Program).Assembly);
await app.Services.ValidateGraphQLClientAssembliesAsync("billing", typeof(Program).Assembly);

app.Run();
```

## See also

- [UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation), the same check from a hosted service
- [IGraphQLClientValidator](/docs/sdk-reference/graphql-client/i-graphql-client-validator), whose `ValidateAssembliesAsync` scans without the ownership rule
- [Check outbound queries against a schema snapshot](/docs/api/check-outbound-queries-against-a-schema-snapshot)
