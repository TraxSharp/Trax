---
title: JwtProviderExtensions
description: Reference for AddTraxGoogleJwtAuth, AddTraxAuth0JwtAuth, AddTraxEntraJwtAuth and AddTraxCognitoJwtAuth, shortcuts that build a provider's authority.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 3
package: Trax.Api.Auth.Jwt
namespace: Trax.Api.Auth.Jwt
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtProviderExtensions.cs
---

# JwtProviderExtensions

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Extension methods on `IServiceCollection` that call [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth)`(authority, audience)` with the authority URL of a common identity provider. Each has a `<TResolver>` overload that passes the resolver through.

## Signatures

```csharp fragment
public static AuthenticationBuilder AddTraxGoogleJwtAuth(this IServiceCollection services, string clientId);
public static AuthenticationBuilder AddTraxGoogleJwtAuth<TResolver>(this IServiceCollection services, string clientId)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;

public static AuthenticationBuilder AddTraxAuth0JwtAuth(this IServiceCollection services, string domain, string audience);
public static AuthenticationBuilder AddTraxAuth0JwtAuth<TResolver>(this IServiceCollection services, string domain, string audience)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;

public static AuthenticationBuilder AddTraxEntraJwtAuth(this IServiceCollection services, string tenantId, string audience);
public static AuthenticationBuilder AddTraxEntraJwtAuth<TResolver>(this IServiceCollection services, string tenantId, string audience)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;

public static AuthenticationBuilder AddTraxCognitoJwtAuth(this IServiceCollection services, string region, string userPoolId, string audience);
public static AuthenticationBuilder AddTraxCognitoJwtAuth<TResolver>(this IServiceCollection services, string region, string userPoolId, string audience)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;
```

## Parameters

| Method | Authority | Audience |
|---|---|---|
| `AddTraxGoogleJwtAuth(clientId)` | `https://accounts.google.com` | The OAuth 2.0 client id, which Google id-tokens carry in `aud`. |
| `AddTraxAuth0JwtAuth(domain, audience)` | `https://{domain}/` | The Auth0 API identifier, not the application's client id. |
| `AddTraxEntraJwtAuth(tenantId, audience)` | `https://login.microsoftonline.com/{tenantId}/v2.0` | The application (client) id or the App ID URI. |
| `AddTraxCognitoJwtAuth(region, userPoolId, audience)` | `https://cognito-idp.{region}.amazonaws.com/{userPoolId}` | The app client id, which Cognito id-tokens carry in `aud`. |

## Returns

The `AuthenticationBuilder` `AddTraxJwtAuth` returns.

## Behavior

Each method registers the default scheme, `JwtDefaults.SchemeName`, exactly as `AddTraxJwtAuth(authority, audience)` does, with the same policies, subscription handling and startup checks.

- `AddTraxAuth0JwtAuth` normalizes `domain`: it trims it, removes a leading `https://` or `http://` and a trailing `/`, and builds `https://{domain}/`. `my-tenant.auth0.com`, `https://my-tenant.auth0.com` and `my-tenant.auth0.com/` give the same authority.
- `AddTraxEntraJwtAuth` takes a directory id, a verified domain such as `contoso.onmicrosoft.com`, or `common` or `organizations`; the last two carry Entra's own signing-key rules.
- `AddTraxCognitoJwtAuth` validates `aud`, so it accepts Cognito id-tokens. For access tokens, which carry the client id in `client_id`, use [UseCognito](/docs/sdk-reference/api-auth/use-cognito).
- A null or blank `domain`, `tenantId`, `region` or `userPoolId` throws `ArgumentException`.

## Example

```csharp compile
services.AddTraxEntraJwtAuth(builder.Configuration["Entra:TenantId"]!, "api://my-app");
```

## See also

- [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth), [UseCognito](/docs/sdk-reference/api-auth/use-cognito)
