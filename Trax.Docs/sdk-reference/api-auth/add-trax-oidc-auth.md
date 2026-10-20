---
title: AddTraxOidcAuth
description: "Reference for AddTraxOidcAuth, the OpenID Connect code-flow scheme: OidcBuilder methods, the default claim mapping, the session cookie and policies."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 5
package: Trax.Api.Auth.Oidc
namespace: Trax.Api.Auth.Oidc
source:
  - Trax.Api/src/Trax.Api.Auth.Oidc/OidcAuthServiceCollectionExtensions.cs
  - Trax.Api/src/Trax.Api.Auth.Oidc/OidcBuilder.cs
  - Trax.Api/src/Trax.Api.Auth.Oidc/DefaultOidcPrincipalResolver.cs
  - Trax.Api/src/Trax.Api.Auth.Oidc/OidcTokenInput.cs
---

# AddTraxOidcAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Extension methods on `IServiceCollection`, declared on `OidcAuthServiceCollectionExtensions`, that register the Trax OpenID Connect authorization-code scheme (`OidcDefaults.SchemeName`) and its session cookie (`OidcDefaults.CookieSchemeName`). A challenge against the OIDC scheme redirects the browser to the identity provider; on the callback the handler validates the id-token, an `ITraxPrincipalResolver<OidcTokenInput>` maps it to a `TraxPrincipal`, and the user is signed into the cookie scheme. For API clients that send tokens, the scheme is [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth).

## Signatures

```csharp fragment
public static AuthenticationBuilder AddTraxOidcAuth(
    this IServiceCollection services,
    string authority,
    string clientId);

public static AuthenticationBuilder AddTraxOidcAuth<TResolver>(
    this IServiceCollection services,
    string authority,
    string clientId)
    where TResolver : class, ITraxPrincipalResolver<OidcTokenInput>;

public static AuthenticationBuilder AddTraxOidcAuth(
    this IServiceCollection services,
    Action<OidcBuilder> configure);

public static AuthenticationBuilder AddTraxOidcAuth<TResolver>(
    this IServiceCollection services,
    Action<OidcBuilder> configure)
    where TResolver : class, ITraxPrincipalResolver<OidcTokenInput>;
```

```csharp fragment
public sealed class OidcBuilder
{
    public OidcBuilder UseAuthority(string authority, string clientId);
    public OidcBuilder WithClientSecret(string clientSecret);
    public OidcBuilder AddScope(string scope);
    public OidcBuilder WithCallbackPath(string callbackPath);
    public OidcBuilder WithSignedOutCallbackPath(string path);
    public OidcBuilder AllowHttpMetadata();
    public OidcBuilder DoNotSaveTokens();
    public OidcBuilder CustomizeOidcOptions(Action<OpenIdConnectOptions> configure);
    public OidcBuilder CustomizeCookieOptions(Action<CookieAuthenticationOptions> configure);
}

public sealed record OidcTokenInput(ClaimsPrincipal Principal, string? IdToken, string? AccessToken);

public sealed class DefaultOidcPrincipalResolver : ITraxPrincipalResolver<OidcTokenInput>
{
    public ValueTask<TraxPrincipal?> ResolveAsync(OidcTokenInput input, CancellationToken ct);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `authority` | `string` | The OIDC issuer URL. `AddTraxOidcAuth(authority, clientId)` is `AddTraxOidcAuth(oidc => oidc.UseAuthority(authority, clientId))`. |
| `clientId` | `string` | The client id registered with the provider. |
| `configure` | `Action<OidcBuilder>` | Configures the scheme. `UseAuthority` is required. |
| `TResolver` | type argument | The resolver, registered scoped. Without it the scheme registers a singleton `DefaultOidcPrincipalResolver`. |

## Members

`OidcBuilder`, passed to `configure`:

| Member | Description |
|---|---|
| `UseAuthority(authority, clientId)` | The issuer URL and client id. |
| `WithClientSecret(secret)` | The secret of a confidential client. Without it the client is public. |
| `AddScope(scope)` | Requests another scope. `openid` and `profile` are requested by default; a scope already present is not added twice. |
| `WithCallbackPath(path)` | Replaces `/signin-oidc`. |
| `WithSignedOutCallbackPath(path)` | Replaces `/signout-callback-oidc`. |
| `AllowHttpMetadata()` | Permits non-HTTPS authority metadata. |
| `DoNotSaveTokens()` | Stops the handler saving the id and access tokens in the session. |
| `CustomizeOidcOptions(configure)` | Edits `OpenIdConnectOptions` after Trax has set them and wired `OnTokenValidated` to the resolver. A second call replaces the first. Replacing `Events` wholesale removes the resolver hook. |
| `CustomizeCookieOptions(configure)` | Edits the session cookie's `CookieAuthenticationOptions` after Trax has set them. A second call replaces the first. |

`OidcTokenInput`, passed to the resolver: `Principal` (the validated id-token's claims), and `IdToken` and `AccessToken` as the raw strings the provider sent, or `null`.

## Returns

The `AuthenticationBuilder` from `services.AddAuthentication()`.

## Behavior

The scheme uses the authorization-code flow (`response_type=code`) with PKCE, saves the tokens in the session unless `DoNotSaveTokens()` is called, requires HTTPS metadata unless `AllowHttpMetadata()` is called, and validates the id-token's issuer, audience, lifetime and signature. `AddTraxOidcAuth` throws `InvalidOperationException` when `UseAuthority` was not called or the callback path is not a valid absolute path.

| Request | Result |
|---|---|
| No session cookie | `AuthenticateResult.NoResult()`. On a protected endpoint the cookie scheme answers 401, not a redirect. |
| Callback id-token invalid (signature, nonce, audience, lifetime) | The handler fails the callback; the user is not signed in. |
| Callback valid, resolver returns `null` | `Fail("OIDC id-token did not map to a known Trax principal.")` |
| Callback valid, resolver throws | Fails with the exception. |
| Callback valid, resolver returns a principal | The user is signed into the cookie scheme; later requests authenticate against the cookie. |

### Session cookie

The cookie is `trax.oidc`: HTTP-only, `SameSite=Lax`, always `Secure`, with an 8-hour sliding expiration. A redirect to a login page is answered with 401 and a redirect to an access-denied page with 403, because the host is an API. `CustomizeCookieOptions` replaces those events for a host with its own login page.

### DefaultOidcPrincipalResolver

Returns `null`, failing the sign-in, when the id-token has no subject.

| Claim | Lands on |
|---|---|
| `sub`, then `ClaimTypes.NameIdentifier` | `TraxPrincipal.Id` |
| The first non-empty of `name`, `ClaimTypes.Name`, `preferred_username`, `email`, `ClaimTypes.Email`, then the id | `TraxPrincipal.DisplayName` |
| `role`, `roles`, `groups`, `ClaimTypes.Role` | `TraxPrincipal.Roles`, without blanks or duplicates |
| Every other claim | `TraxPrincipal.Claims`, the first value of each type |
| | `TraxPrincipal.PrincipalType` is `oidc`. |

### Policies

`OidcDefaults.PolicyName` authenticates against the cookie scheme and is satisfied only by a caller the OIDC session signed in. The cookie scheme joins `TraxAuthClaimTypes.TraxAuthPolicy`. On the Trax GraphQL endpoint, the builder's `RequireAuthorization(...)` is the gate the startup checks for the `operations` namespace and `[TraxAllowAnonymous]` see; a convention on the route is an extra layer those checks do not count. See [Authorization](/docs/security/authorization-rules#combining-with-endpoint-level-auth).

### Subscriptions

The session cookie authenticates the WebSocket upgrade like any other request. The schema accepts an upgrade only from origins the host serves: the endpoint's own host, or the origins `AllowSocketOrigins(...)` allows, which default to the origins the CORS default policy names. A policy built with `AllowAnyOrigin()` names none. The socket closes when the sign-in it opened with expires, or at the connection's maximum lifetime. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#allowed-origins).

### Registered services

| Service | Lifetime |
|---|---|
| Authentication services, the `TraxOidc` OpenID Connect scheme and the `TraxOidc.Cookie` cookie scheme | |
| `ITraxPrincipalResolver<OidcTokenInput>`: `DefaultOidcPrincipalResolver` or `TResolver` | Singleton or scoped |
| `OidcPolicy`, and the cookie scheme in `TraxAuthPolicy` | |
| [`TraxPrincipal` and `TraxCaller`](/docs/sdk-reference/api-auth/add-trax-principal-accessor), through `AddTraxPrincipalAccessor()` | Scoped |
| The NO WARRANTY startup log, registered once | Hosted service |

## Examples

A public client:

```csharp compile
services.AddTraxOidcAuth("https://login.example.com", "my-client-id");
```

A confidential client with another scope, gated GraphQL, and a sign-in route:

```csharp compile
services.AddTraxOidcAuth(oidc => oidc
    .UseAuthority("https://login.example.com", "my-client-id")
    .WithClientSecret(builder.Configuration["Oidc:ClientSecret"]!)
    .AddScope("email"));

services.AddTraxGraphQL(graphql => graphql.RequireAuthorization(OidcDefaults.PolicyName));

app.MapGet("/login", () =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OidcDefaults.SchemeName]));
```

A resolver for Entra's `oid` and `groups` claims:

```csharp compile
services.AddTraxOidcAuth<EntraOidcResolver>(oidc =>
    oidc.UseAuthority("https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0", "my-client-id"));

public sealed class EntraOidcResolver : ITraxPrincipalResolver<OidcTokenInput>
{
    public ValueTask<TraxPrincipal?> ResolveAsync(OidcTokenInput input, CancellationToken ct)
    {
        var oid = input.Principal.FindFirst("oid")?.Value;
        if (oid is null)
            return ValueTask.FromResult<TraxPrincipal?>(null);

        var groups = input.Principal.FindAll("groups").Select(c => c.Value).ToArray();
        var name = input.Principal.FindFirst("name")?.Value ?? oid;
        return ValueTask.FromResult<TraxPrincipal?>(
            new TraxPrincipal(oid, name, groups, PrincipalType: OidcDefaults.PrincipalType));
    }
}
```

## See also

- [OpenID Connect authentication](/docs/security/secure-a-graphql-host#1-write-the-host)
- [OidcDefaults](/docs/sdk-reference/api-auth/oidc-defaults), [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver)
