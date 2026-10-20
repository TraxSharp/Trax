---
title: AddKeyedTraxGraphQLClient
description: "Reference for AddKeyedTraxGraphQLClient, which registers a GraphQL client under a DI key so clients for several servers live in one container."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 2
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Extensions/ServiceExtensions.cs
---

# AddKeyedTraxGraphQLClient

Registers a GraphQL client under a DI service key, so clients for different servers live in one
container. Each key gets its own configuration, `HttpClient`, schema provider and validator cache.
Declared on the static class `ServiceExtensions`.

## Signature

```csharp fragment
public static TraxGraphQLClientBuilder AddKeyedTraxGraphQLClient(
    this IServiceCollection services,
    object serviceKey,
    Uri baseAddress)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `services` | `IServiceCollection` | Yes | The service collection |
| `serviceKey` | `object` | Yes | The key that names the downstream server, usually a string or an enum value. Compared with `Equals` |
| `baseAddress` | `Uri` | Yes | The GraphQL endpoint for this key |

## Returns

`TraxGraphQLClientBuilder`. Every builder method applies to this key's registration only.

## Behavior

- Throws `ArgumentNullException` when any argument is `null`.
- Registers the same four services as
  [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client#registered-services),
  each as a keyed singleton under `serviceKey`. Each keyed service resolves its own dependencies by
  the same key, so a keyed executor validates against its own server's schema.
- The `IHttpClientFactory` client is named after the key's type and value
  (`Trax.Api.GraphQL.Client:<key type>:<key>`), so the string key `"Billing"` and an enum value
  `Billing` get different clients.
- Validation for a keyed client (`UseStartupValidation` on its builder, or the keyed overload of
  [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies))
  checks only the request types marked
  [`[GraphQLClient(key)]`](/docs/sdk-reference/graphql-client/graphql-client-attribute) with its key.
  The unkeyed client's validation checks only the unmarked ones.
- A keyed and an unkeyed client can be registered side by side. Two calls with the same key behave
  like two `AddTraxGraphQLClient` calls: the later registration wins.
- Resolve without attributes with `GetRequiredKeyedService<IGraphQLClientExecutor>(key)`.

## Example

```csharp compile
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
    public async Task<(Invoice, Shipment)> Load(string invoiceId, string orderId, CancellationToken ct) =>
        (
            await billing.Run(new GetInvoiceRequest { Id = invoiceId }, ct),
            await shipping.Run(new GetShipmentRequest { OrderId = orderId }, ct)
        );
}
```

## See also

- [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client)
- [GraphQLClientAttribute](/docs/sdk-reference/graphql-client/graphql-client-attribute)
- [GraphQL Client](/docs/api/call-another-graphql-server)
