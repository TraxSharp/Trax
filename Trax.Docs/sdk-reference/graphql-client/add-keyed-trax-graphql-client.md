---
layout: default
title: AddKeyedTraxGraphQLClient
description: Reference for AddKeyedTraxGraphQLClient, which registers a GraphQL client under a DI key so clients for several servers live in one container.
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 2
---

# AddKeyedTraxGraphQLClient

Registers a GraphQL client under a DI service key, so clients for different servers can live in one container. Each key gets its own configuration, `HttpClient`, schema provider and validator cache.

## Signature

```csharp
public static TraxGraphQLClientBuilder AddKeyedTraxGraphQLClient(
    this IServiceCollection services,
    object serviceKey,
    Uri baseAddress)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceCollection` | Yes | The service collection |
| `serviceKey` | `object` | Yes | The key that names the downstream server. Usually a string or an enum value. The library never inspects it beyond comparing it with `Equals` |
| `baseAddress` | `Uri` | Yes | The GraphQL endpoint for this key |

**Returns**: `TraxGraphQLClientBuilder`. Every builder method applies to this key's registration only.

**Throws**: `ArgumentNullException` when any argument is `null`.

## What it registers

The same four services as [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client#what-it-registers), each as a keyed singleton under `serviceKey`. Each keyed service resolves its own dependencies by the same key, so a keyed executor always validates against its own server's schema.

## Example

```csharp
services.AddKeyedTraxGraphQLClient("billing", new Uri("https://billing.example.com/graphql"))
        .UseFileSchema("billing.graphql")
        .UseStartupValidation(typeof(Program).Assembly);

services.AddKeyedTraxGraphQLClient("shipping", new Uri("https://shipping.example.com/graphql"))
        .UseFileSchema("shipping.graphql")
        .UseStartupValidation(typeof(Program).Assembly);

public class OrderSync(
    [FromKeyedServices("billing")] IGraphQLClientExecutor billing,
    [FromKeyedServices("shipping")] IGraphQLClientExecutor shipping)
{
    // ...
}
```

Resolve without attributes with `GetRequiredKeyedService<IGraphQLClientExecutor>("billing")`.

## Validating a keyed client's requests

Mark each request with the key of the client it belongs to:

```csharp
[GraphQLClient("billing")]
public sealed class GetInvoiceRequest : IGraphQLClientRequest<Invoice> { /* ... */ }
```

Validation for a keyed client (`UseStartupValidation` on its builder, or the keyed overload of [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-assemblies)) checks only the request types marked with its key. Validation for the unkeyed client checks only the unmarked ones. The key is compared by value, so an enum key works as well as a string.

`[GraphQLClient]` steers validation only. Which server a request is sent to is decided by the executor you run it through.

## Remarks

- A keyed and an unkeyed client can be registered side by side.
- Calling `AddKeyedTraxGraphQLClient` twice with the same key behaves like calling `AddTraxGraphQLClient` twice: the later registration wins.

## Package

```
dotnet add package Trax.Api.GraphQL.Client
```
