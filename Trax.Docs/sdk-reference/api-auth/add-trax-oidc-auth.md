---
layout: default
title: AddTraxOidcAuth
parent: API Auth
grand_parent: SDK Reference
---

# AddTraxOidcAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Registers the Trax OpenID Connect code-flow scheme (`OidcDefaults.SchemeName`), its session cookie (`OidcDefaults.CookieSchemeName`), the `OidcDefaults.PolicyName` authorization policy, the combined `TraxAuthPolicy`, `IHttpContextAccessor`, and a one-shot startup disclaimer log.

OIDC is a browser protocol. A challenge against the OIDC scheme issues a redirect to the identity provider; on callback, the handler validates the id-token and signs the user into the cookie scheme. Subsequent requests authorize against the cookie, not the OIDC scheme. For token-based API clients, use [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) instead.

## Signatures

```csharp
// Shortest path: authority + clientId, default scopes (openid, profile).
public static AuthenticationBuilder AddTraxOidcAuth(
    this IServiceCollection services,
    string authority,
    string clientId);

public static AuthenticationBuilder AddTraxOidcAuth<TResolver>(
    this IServiceCollection services,
    string authority,
    string clientId)
    where TResolver : class, ITraxPrincipalResolver<OidcTokenInput>;

// Full control: client secret, extra scopes, callback paths, event handlers.
public static AuthenticationBuilder AddTraxOidcAuth(
    this IServiceCollection services,
    Action<OidcBuilder> configure);

public static AuthenticationBuilder AddTraxOidcAuth<TResolver>(
    this IServiceCollection services,
    Action<OidcBuilder> configure)
    where TResolver : class, ITraxPrincipalResolver<OidcTokenInput>;
```

## Basic configuration

For public clients (SPA, native) with PKCE, the positional overload covers the common case:

```csharp
services.AddTraxOidcAuth("https://login.example.com", "my-client-id");
```

For confidential clients or extra scopes, use the builder:

```csharp
services.AddTraxOidcAuth(oidc => oidc
    .UseAuthority("https://login.example.com", "my-client-id")
    .WithClientSecret(builder.Configuration["Oidc:ClientSecret"]!)
    .AddScope("email"));
```

PKCE is enabled by default on both paths.

## Claim mapping

The default resolver reads standard OIDC claims:

| Claim | Maps to |
|---|---|
| `sub` (then `nameidentifier`) | `TraxPrincipal.Id` |
| `name` (then `preferred_username`, `email`) | `TraxPrincipal.DisplayName` |
| `role`, `roles`, `groups` | `TraxPrincipal.Roles` |
| Everything else | `TraxPrincipal.Claims` (verbatim) |

For provider-specific mapping (Okta, Auth0, Entra, Cognito), supply a custom resolver:

```csharp
public sealed class EntraOidcResolver : ITraxPrincipalResolver<OidcTokenInput>
{
    public ValueTask<TraxPrincipal?> ResolveAsync(OidcTokenInput input, CancellationToken ct)
    {
        var oid = input.Principal.FindFirst("oid")?.Value;
        if (oid is null) return new(default(TraxPrincipal));

        var groups = input.Principal.FindAll("groups").Select(c => c.Value).ToArray();
        return new(new TraxPrincipal(oid, input.Principal.FindFirst("name")?.Value ?? oid, groups,
            PrincipalType: OidcDefaults.PrincipalType));
    }
}

services.AddTraxOidcAuth<EntraOidcResolver>(oidc => oidc.UseAuthority("...", "..."));
```

## `OidcBuilder` methods

| Method | Purpose |
|---|---|
| `UseAuthority(string authority, string clientId)` | Required. OIDC issuer URL and registered client id. |
| `WithClientSecret(string)` | Confidential-client secret. Omit for public clients. |
| `AddScope(string)` | Request additional scopes. `openid` and `profile` are registered by default. |
| `WithCallbackPath(string)` | Override the default `/signin-oidc`. |
| `WithSignedOutCallbackPath(string)` | Override the default `/signout-callback-oidc`. |
| `AllowHttpMetadata()` | Permit non-HTTPS authority metadata. Dev/test only. |
| `DoNotSaveTokens()` | Disable storing id/access tokens on the cookie. |
| `CustomizeOidcOptions(Action<OpenIdConnectOptions>)` | Raw access to OIDC options (event handlers, protocol validator). Do not overwrite `Events` wholesale. |
| `CustomizeCookieOptions(Action<CookieAuthenticationOptions>)` | Session cookie tweaks (expiration, SameSite, domain). |

## Session cookie

The cookie is `trax.oidc`, HTTP-only, `SameSite=Lax`, `Secure`, with a sliding 8-hour expiration. Unauthenticated requests return `401` (not a redirect to `/Account/Login`), because this is an API-auth package. If you embed an MVC login page, override via `CustomizeCookieOptions`:

```csharp
services.AddTraxOidcAuth(oidc => oidc
    .UseAuthority("...", "...")
    .CustomizeCookieOptions(cookie =>
    {
        cookie.LoginPath = "/login";
        cookie.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    }));
```

## Protecting endpoints

Authorize against the cookie scheme (not the OIDC scheme):

```csharp
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization(OidcDefaults.PolicyName));

// Or the combined Trax policy to allow API-key / JWT / OIDC interchangeably:
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy));
```

Issue a sign-in challenge by challenging the OIDC scheme:

```csharp
app.MapGet("/login", (HttpContext ctx) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/" },
        new[] { OidcDefaults.SchemeName }));
```

## Return Semantics

| Condition | Result |
|---|---|
| No session cookie | `AuthenticateResult.NoResult()` (permits `[AllowAnonymous]`) |
| Callback id-token invalid (signature, nonce, audience, lifetime) | Handler fails the callback; user is not signed in |
| Callback valid, resolver returns `null` | `context.Fail("OIDC id-token did not map to a known Trax principal.")` |
| Callback valid, resolver throws | `context.Fail(exception)` |
| Callback valid, resolver returns `TraxPrincipal` | User signed into the cookie scheme; subsequent requests authenticate against the cookie |
