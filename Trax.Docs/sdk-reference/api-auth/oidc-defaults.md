---
title: OidcDefaults
description: Reference for OidcDefaults, the constants of the Trax OpenID Connect scheme, its scheme, cookie and policy names, callback paths and principal type.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 15
package: Trax.Api.Auth.Oidc
namespace: Trax.Api.Auth.Oidc
source:
  - Trax.Api/src/Trax.Api.Auth.Oidc/OidcDefaults.cs
---

# OidcDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The constants of the Trax OpenID Connect scheme that [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth) registers.

## Signature

```csharp fragment
public static class OidcDefaults
{
    public const string SchemeName = "TraxOidc";
    public const string CookieSchemeName = "TraxOidc.Cookie";
    public const string PolicyName = "OidcPolicy";
    public const string CallbackPath = "/signin-oidc";
    public const string SignedOutCallbackPath = "/signout-callback-oidc";
    public const string PrincipalType = "oidc";
}
```

## Members

| Constant | Value | Description |
|---|---|---|
| `SchemeName` | `TraxOidc` | The challenge scheme. A challenge against it redirects the browser to the identity provider. |
| `CookieSchemeName` | `TraxOidc.Cookie` | The session cookie scheme. Requests after sign-in authenticate against it. |
| `PolicyName` | `OidcPolicy` | The policy bound to the cookie scheme, satisfied only by a caller the OIDC session authenticated. |
| `CallbackPath` | `/signin-oidc` | The default callback path. `OidcBuilder.WithCallbackPath` overrides it. |
| `SignedOutCallbackPath` | `/signout-callback-oidc` | The default sign-out callback path. `OidcBuilder.WithSignedOutCallbackPath` overrides it. |
| `PrincipalType` | `oidc` | The `trax:principal-type` claim `DefaultOidcPrincipalResolver` writes. |

## Example

```csharp compile
app.MapGet("/login", () =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OidcDefaults.SchemeName]));
```

## See also

- [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth), [TraxAuthClaimTypes](/docs/sdk-reference/api-auth/trax-auth-claim-types)
