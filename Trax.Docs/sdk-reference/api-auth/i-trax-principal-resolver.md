---
title: ITraxPrincipalResolver
description: Reference for ITraxPrincipalResolver, which maps a verified API key, JWT or OIDC sign-in to a TraxPrincipal, with its return semantics and lifetime.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 11
package: Trax.Api.Auth
namespace: Trax.Api.Auth
source:
  - Trax.Api/src/Trax.Api.Auth/ITraxPrincipalResolver.cs
---

# ITraxPrincipalResolver

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Maps a credential to a [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal). Generic over the input, so one abstraction serves every Trax scheme.

## Signature

```csharp fragment
public interface ITraxPrincipalResolver<in TInput>
{
    ValueTask<TraxPrincipal?> ResolveAsync(TInput input, CancellationToken ct);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `input` | `TInput` | The credential, by scheme: see the table below. |
| `ct` | `CancellationToken` | The request's `RequestAborted` token. |

| Scheme | `TInput` | What the resolver receives |
|---|---|---|
| API key | `string` | The raw header value. The resolver decides whether it is valid. |
| JWT bearer | `JwtTokenInput` | The token after `JwtBearerHandler` validated its signature, issuer, audience and lifetime: `Principal` (the token's claims) and `SecurityToken`. |
| OpenID Connect | `OidcTokenInput` | The sign-in after the OIDC handler validated the id-token: `Principal`, and `IdToken` and `AccessToken` as raw strings when the provider sent them. |

## Returns

The principal, or `null` when the input does not match a known principal. The scheme handler fails authentication on `null`.

## Behavior

- A resolver that throws fails authentication. The API-key handler logs the exception at `Warning` and fails with `API-key resolver failed.`; the JWT and OIDC handlers fail with the exception.
- Lifetime, by registration:

| Registration | Lifetime |
|---|---|
| `AddTraxApiKeyAuth<TResolver>()`, `AddTraxJwtAuth<TResolver>(...)`, `AddTraxOidcAuth<TResolver>(...)` | Scoped, resolved per request. |
| `AddTraxApiKeyAuth(keys => ...)` | Singleton [HashedApiKeyResolver](/docs/sdk-reference/api-auth/hashed-api-key-resolver) over the configured keys, immutable once built. |
| `AddTraxJwtAuth(...)` without `TResolver` | Singleton `DefaultJwtPrincipalResolver`. |
| `AddTraxOidcAuth(...)` without `TResolver` | Singleton `DefaultOidcPrincipalResolver`. |

- On a GraphQL subscription the resolver is scoped per connection: the socket interceptor opens a scope when `connection_init` arrives and disposes it once the principal is resolved. See [subscriptions](/docs/sdk-reference/graphql-api/subscriptions#authentication).

## Example

```csharp compile
services.AddTraxJwtAuth<DirectoryJwtResolver>("https://login.example.com", "my-api");

public sealed class DirectoryJwtResolver(IUserDirectory users) : ITraxPrincipalResolver<JwtTokenInput>
{
    public async ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct)
    {
        var sub = input.Principal.FindFirst("sub")?.Value;
        if (sub is null)
            return null;

        var user = await users.FindByIdAsync(sub, ct);
        if (user is null || user.IsRevoked)
            return null;

        return new TraxPrincipal(user.Id, user.DisplayName, user.Roles,
            Claims: new Dictionary<string, string> { ["tenant"] = user.TenantId },
            PrincipalType: JwtDefaults.PrincipalType);
    }
}
```

## See also

- [Write a JWT principal resolver](/docs/security/write-a-jwt-principal-resolver)
- [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth), [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth), [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth)
