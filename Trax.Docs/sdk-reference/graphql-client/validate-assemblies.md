---
layout: default
title: ValidateGraphQLClientAssembliesAsync
description: Reference for ValidateGraphQLClientAssembliesAsync, which checks every request type a client owns against its schema at startup or in a test.
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 4
---

# ValidateGraphQLClientAssembliesAsync

Validates every request type a client owns in a set of assemblies against that client's schema, so a query the server no longer accepts fails at startup or in a test instead of on first use. Call it after `app.Build()`.

## Signatures

```csharp
public static Task ValidateGraphQLClientAssembliesAsync(
    this IServiceProvider services,
    params Assembly[] assemblies)

public static Task ValidateGraphQLClientAssembliesAsync(
    this IServiceProvider services,
    object serviceKey,
    params Assembly[] assemblies)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceProvider` | Yes | The built container, usually `app.Services` |
| `serviceKey` | `object` | Keyed overload | The key the client was registered with by [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client) |
| `assemblies` | `Assembly[]` | Yes | The assemblies to scan |

**Returns**: a `Task` that completes when every request has validated.

## Which requests are checked

Every concrete class in `assemblies` that implements `IGenericGraphQLClientRequest` and belongs to the client:

- The unkeyed overload checks the types that carry no `[GraphQLClient]` mark, against the unkeyed client's schema.
- The keyed overload checks the types marked `[GraphQLClient(serviceKey)]`, against the schema of the client registered under that key.

Each type is created with `RuntimeHelpers.GetUninitializedObject`, without running a constructor, and its `Query` is validated. A `Query` must therefore not depend on state a constructor sets. A literal string, an embedded resource ([GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request)) and a `TypedRequest` all qualify. Validation stops at the first failure.

## Exceptions

| Exception | When |
|-----------|------|
| `GraphQLValidationException` | A query does not validate against the schema, has no operation, or has more than one. `Query` holds the failing text and `Errors` the validator's errors |
| `GraphQLSchemaIntrospectionException` | The schema could not be loaded (introspection failed, the SDL file is missing or invalid) |
| `InvalidOperationException` | No request type in `assemblies` belongs to this client, or (keyed overload) no client is registered under `serviceKey` |

Finding nothing to validate is refused on purpose: it almost always means a request was left unmarked or marked with a misspelt key, and passing would hide that.

## Example

```csharp
var app = builder.Build();

await app.Services.ValidateGraphQLClientAssembliesAsync(typeof(Program).Assembly);
await app.Services.ValidateGraphQLClientAssembliesAsync("billing", typeof(Program).Assembly);

app.Run();
```

In a test, the same call over a container built with `UseFileSchema` checks every query against the checked-in schema with no server running.

## Validating with a custom filter

`IGraphQLClientValidator`, which every client registers, carries an extension for scanning without the ownership rule:

```csharp
public static Task ValidateAssembliesAsync(
    this IGraphQLClientValidator validator,
    IEnumerable<Assembly> assemblies,
    CancellationToken cancellationToken = default)

public static Task ValidateAssembliesAsync(
    this IGraphQLClientValidator validator,
    IEnumerable<Assembly> assemblies,
    Func<Type, bool>? typeFilter,
    CancellationToken cancellationToken = default)
```

It validates every request type `typeFilter` accepts (all of them when `null`), whatever its `[GraphQLClient]` mark, and does not fail when it finds none.

## Remarks

- For host-startup gating, `UseStartupValidation` on the [builder](/docs/sdk-reference/graphql-client/builder#usestartupvalidation) does the same check from a hosted service, so the host refuses to start on a failure.
- The validator caches each query text that passes, so validating at startup also warms the cache the executor uses.

## Package

```
dotnet add package Trax.Api.GraphQL.Client
```
