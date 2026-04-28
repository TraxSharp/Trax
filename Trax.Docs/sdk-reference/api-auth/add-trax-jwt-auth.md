---
layout: default
title: AddTraxJwtAuth
parent: API Auth
grand_parent: SDK Reference
---

# AddTraxJwtAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Registers the Trax JWT bearer authentication scheme, its authorization policy (`JwtDefaults.PolicyName`), the combined `TraxAuthPolicy`, `IHttpContextAccessor`, and a one-shot startup disclaimer log.

Token validation (signature, issuer, audience, lifetime) is delegated to `Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler`. After validation, Trax runs an `ITraxPrincipalResolver<JwtTokenInput>` to project the validated token into a `TraxPrincipal`. A resolver that returns `null` fails authentication.

## Signatures

```csharp
// Shortest path: OIDC authority + audience, default claim mapping.
public static AuthenticationBuilder AddTraxJwtAuth(
    this IServiceCollection services,
    string authority,
    string audience);

public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
    this IServiceCollection services,
    string authority,
    string audience)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;

// Full control: symmetric keys, custom token validation, event handlers.
public static AuthenticationBuilder AddTraxJwtAuth(
    this IServiceCollection services,
    Action<JwtBuilder> configure);

public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
    this IServiceCollection services,
    Action<JwtBuilder> configure)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;
```

| Overload | Use when | Resolver lifetime |
|---|---|---|
| `AddTraxJwtAuth(authority, audience)` | OIDC-backed API, default claim mapping is fine. | Singleton `DefaultJwtPrincipalResolver`. |
| `AddTraxJwtAuth<TResolver>(authority, audience)` | OIDC-backed API, resolver enriches from DB / cache. | Scoped, resolved from DI per request. |
| `AddTraxJwtAuth(Action<JwtBuilder>)` | Symmetric key, custom validation, or event handlers. | Singleton `DefaultJwtPrincipalResolver`. |
| `AddTraxJwtAuth<TResolver>(Action<JwtBuilder>)` | All of the above + custom resolver. | Scoped, resolved from DI per request. |

The positional overload is sugar for `AddTraxJwtAuth(jwt => jwt.UseAuthority(authority, audience))`. Reach for the builder when you need symmetric keys, clock-skew tweaks, or `OnChallenge`/`OnAuthenticationFailed` handlers.

```csharp
// One-liner for the common OIDC-backed API case:
services.AddTraxJwtAuth("https://login.example.com", "my-api");
```

## Provider shortcuts

Authority URLs for the common identity providers have non-obvious shapes. These wrappers in `Trax.Api.Auth.Jwt` hide them:

```csharp
// Google — authority baked to https://accounts.google.com
services.AddTraxGoogleJwtAuth(oauthClientId);

// Auth0 — authority baked to https://{domain}/ (trailing slash required by Auth0)
services.AddTraxAuth0JwtAuth("my-tenant.auth0.com", "https://api.example.com");

// Microsoft Entra (v2.0) — authority baked to
// https://login.microsoftonline.com/{tenantId}/v2.0
services.AddTraxEntraJwtAuth(tenantId, "api://my-app");

// Amazon Cognito — authority baked to
// https://cognito-idp.{region}.amazonaws.com/{userPoolId}
services.AddTraxCognitoJwtAuth("us-east-1", "us-east-1_AbCdEfGhI", "app-client-id");
```

| Helper | Authority built | Audience semantics |
|---|---|---|
| `AddTraxGoogleJwtAuth(clientId)` | `https://accounts.google.com` | OAuth 2.0 client id (id-token `aud`) |
| `AddTraxAuth0JwtAuth(domain, audience)` | `https://{domain}/` (normalized) | Auth0 API identifier, not the client id |
| `AddTraxEntraJwtAuth(tenantId, audience)` | `https://login.microsoftonline.com/{tenantId}/v2.0` | Application (client) ID or App ID URI |
| `AddTraxCognitoJwtAuth(region, userPoolId, audience)` | `https://cognito-idp.{region}.amazonaws.com/{userPoolId}` | Cognito app client id (id-token path) |

Each helper has a `<TResolver>` overload when you need claim-to-principal enrichment beyond the default. Behind the scenes they delegate to `AddTraxJwtAuth(authority, audience)`, so the combined `TraxAuthPolicy`, subscription interceptor, and everything else on this page applies.

The Auth0 helper normalizes the `domain` argument: strips `https://` / `http://` prefixes, strips a trailing slash, re-wraps to `https://{domain}/`. Pass `my-tenant.auth0.com`, `https://my-tenant.auth0.com`, or `my-tenant.auth0.com/` — all resolve identically.

Entra's `tenantId` can be a directory GUID, a verified domain (`contoso.onmicrosoft.com`), or one of the multi-tenant sentinels (`common`, `organizations`). The sentinels come with their own signing-key validation rules; prefer a specific tenant unless you explicitly need multi-tenancy.

## OIDC authority (JWKS)

Fetches signing keys from a provider's discovery document. Keys rotate on the provider's cadence; the handler caches and refreshes automatically.

```csharp
services.AddTraxJwtAuth(jwt => jwt.UseAuthority(
    authority: "https://login.example.com",
    audience:  "my-api"));
```

## Explicit signing key

For services that mint their own tokens, or when key material is loaded from a secret manager:

```csharp
var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!);

services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(
    issuer:   "https://trax.internal",
    audience: "my-api",
    key:      key));
```

`UseSymmetricKey` requires at least 32 bytes (HS256 minimum). For RSA or EC, use `UseSigningKey(issuer, audience, SecurityKey)` with an `RsaSecurityKey` or `ECDsaSecurityKey`.

## Do you actually need a custom resolver?

Most apps don't. The positional overload plus the default resolver is the whole integration:

```csharp
services.AddTraxJwtAuth("https://login.example.com", "my-api");
```

`DefaultJwtPrincipalResolver` maps the standard OIDC claims:

| Token claim | Lands on |
|---|---|
| `sub` (then `nameidentifier`) | `TraxPrincipal.Id` |
| `name` (then `preferred_username`, `email`) | `TraxPrincipal.DisplayName` |
| `role`, `roles`, `ClaimTypes.Role` | `TraxPrincipal.Roles` |
| Everything else | `TraxPrincipal.Claims` (verbatim, with Trax-reserved claim types filtered out) |

If your provider emits those claims (Google, Auth0, Entra, Cognito, Okta all do), you write zero code. `TraxPrincipal` injection, per-train `[TraxAuthorize]`, and role checks all work end to end.

Reach for a custom resolver only when one of these is true:

- **Roles live in a non-standard claim.** Entra app roles, Okta group URIs, namespaced Auth0 claims.
- **You need database enrichment.** Look up the user's tenant, permissions, feature flags — anything that isn't already in the token.
- **You need to reject unknown subjects.** Allow-list of provisioned users, revocation cache, account suspension check.
- **You need to transform claims.** Strip a namespace prefix, coerce numeric subjects to strings, merge multiple role sources into one.

## Custom resolver

When the token's `sub` needs to be matched against a user record (tenant lookup, role enrichment, revocation check):

```csharp
public sealed class MyJwtResolver(AppDbContext db) : ITraxPrincipalResolver<JwtTokenInput>
{
    public async ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct)
    {
        var sub = input.Principal.FindFirst("sub")?.Value;
        if (sub is null) return null;

        var user = await db.Users.FindAsync([sub], ct);
        if (user is null || user.IsRevoked) return null;

        return new TraxPrincipal(user.Id, user.DisplayName, user.Roles,
            Claims: new Dictionary<string, string> { ["tenant"] = user.TenantId });
    }
}

services.AddTraxJwtAuth<MyJwtResolver>(jwt => jwt.UseAuthority("...", "..."));
```

The resolver is resolved per request, so scoped dependencies (DbContext, HTTP client with scoped state) work as expected.

## `JwtBuilder` methods

| Method | Purpose |
|---|---|
| `UseAuthority(string authority, string audience)` | Fetch signing keys from the provider's JWKS endpoint. |
| `UseSymmetricKey(string issuer, string audience, byte[] key)` | Explicit HS256 key. Minimum 32 bytes. |
| `UseSigningKey(string issuer, string audience, SecurityKey key)` | Arbitrary signing key (RSA, EC, symmetric). |
| `WithClockSkew(TimeSpan)` | Override the 5-minute default skew. `TimeSpan.Zero` for strict validation. |
| `AllowHttpMetadata()` | Permit non-HTTPS authority metadata. Dev/test only. |
| `CustomizeTokenValidation(Action<TokenValidationParameters>)` | Last-writer hook for `TokenValidationParameters` (custom audience lists, lifetime validators, type validation). |
| `CustomizeBearerOptions(Action<JwtBearerOptions>)` | Raw access to `JwtBearerOptions` for event handlers (`OnChallenge`, `OnAuthenticationFailed`). Do not overwrite `Events` wholesale. |

`UseAuthority` and `UseSigningKey` are mutually exclusive. Calling neither, or both, throws `InvalidOperationException` at startup.

## Return Semantics

| Condition | Result |
|---|---|
| No `Authorization: Bearer` header | `AuthenticateResult.NoResult()` (permits `[AllowAnonymous]`) |
| Token signature, issuer, audience, or lifetime invalid | `AuthenticateResult.Fail(...)` |
| Token valid, resolver returns `null` | `AuthenticateResult.Fail("JWT did not map to a known Trax principal.")` |
| Token valid, resolver throws | `AuthenticateResult.Fail(exception)` |
| Token valid, resolver returns `TraxPrincipal` | `AuthenticateResult.Success(ticket)` |

## Protecting Endpoints

`AddTraxJwtAuth` does not set a default authentication scheme. Name the scheme or policy explicitly:

```csharp
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization(JwtDefaults.PolicyName));

// Mix with other Trax schemes:
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy));
```

Combining JWT with API-key or OIDC routes credentials through whichever scheme the presented header matches.

## Subscriptions

When `Trax.Api.GraphQL` is also present, `AddTraxJwtAuth` auto-registers `TraxJwtSocketInterceptor`. Subscriptions receive the token through the `connection_init` payload:

```js
ws.send(JSON.stringify({
    type: "connection_init",
    payload: { authToken: "<jwt>" }  // or "bearer"
}));
```

The interceptor validates against the same `JwtBearerOptions` as the HTTP handler (signature, issuer, audience, lifetime, clock skew), then runs the principal resolver and attaches the result to `HttpContext.User` for the socket lifetime. Rejected connections close before any subscription operation runs.
