---
title: ApiKeyDefaults
description: Reference for ApiKeyDefaults, the constants of the Trax API-key authentication scheme, its scheme name, policy name and the default X-Api-Key header.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 8
package: Trax.Api.Auth.ApiKey
namespace: Trax.Api.Auth.ApiKey
source:
  - Trax.Api/src/Trax.Api.Auth.ApiKey/ApiKeyDefaults.cs
---

# ApiKeyDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

The constants of the Trax API-key scheme that [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth) registers.

## Signature

```csharp fragment
public static class ApiKeyDefaults
{
    public const string SchemeName = "TraxApiKey";
    public const string PolicyName = "ApiKeyPolicy";
    public const string HeaderName = "X-Api-Key";
}
```

## Members

| Constant | Value | Description |
|---|---|---|
| `SchemeName` | `TraxApiKey` | The authentication scheme name. A principal it authenticates carries the id `TraxApiKey:{id}`. |
| `PolicyName` | `ApiKeyPolicy` | The authorization policy `AddTraxApiKeyAuth` registers. It is satisfied only by a caller the API-key scheme authenticated, wherever it is evaluated: the endpoint gate, `GateOperations`, `[TraxAuthorize(Policy = ...)]`, sockets. |
| `HeaderName` | `X-Api-Key` | The default request header. [ApiKeyAuthenticationOptions.HeaderName](/docs/sdk-reference/api-auth/api-key-authentication-options) overrides it. |

## Example

```csharp compile
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization(ApiKeyDefaults.PolicyName));

app.MapGet("/reports", () => Results.Ok()).RequireAuthorization(ApiKeyDefaults.PolicyName);
```

## See also

- [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth), [TraxAuthClaimTypes](/docs/sdk-reference/api-auth/trax-auth-claim-types)
