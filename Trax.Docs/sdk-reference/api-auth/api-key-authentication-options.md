---
title: ApiKeyAuthenticationOptions
description: Reference for ApiKeyAuthenticationOptions, the options AddTraxApiKeyAuth passes to the API-key authentication scheme, such as the header it reads.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 6
package: Trax.Api.Auth.ApiKey
namespace: Trax.Api.Auth.ApiKey
source:
  - Trax.Api/src/Trax.Api.Auth.ApiKey/ApiKeyAuthenticationOptions.cs
---

# ApiKeyAuthenticationOptions

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The options of the API-key scheme, set through the `configureOptions` argument of [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth). It derives from `AuthenticationSchemeOptions` and has all of its members.

## Signature

```csharp fragment
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; }
}
```

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `HeaderName` | `string` | `"X-Api-Key"` ([ApiKeyDefaults.HeaderName](/docs/sdk-reference/api-auth/api-key-defaults)) | The request header that carries the key. |

## Example

```csharp compile
services.AddTraxApiKeyAuth<DirectoryApiKeyResolver>(opts => opts.HeaderName = "X-Service-Key");

public sealed class DirectoryApiKeyResolver(IUserDirectory users) : ITraxPrincipalResolver<string>
{
    public async ValueTask<TraxPrincipal?> ResolveAsync(string apiKey, CancellationToken ct)
    {
        var user = await users.FindByApiKeyHashAsync(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)), ct);
        return user is null ? null : new TraxPrincipal(user.Id, user.DisplayName, user.Roles);
    }
}
```

## See also

- [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth), [ApiKeyDefaults](/docs/sdk-reference/api-auth/api-key-defaults)
