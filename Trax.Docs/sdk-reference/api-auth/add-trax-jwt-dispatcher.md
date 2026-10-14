---
title: AddTraxJwtDispatcher
description: Reference for AddTraxJwtDispatcher, which routes a Bearer token to the JWT scheme its iss claim maps to, JwtDispatcherBuilder and JwtDispatcherRuntime.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 4
package: Trax.Api.Auth.Jwt
namespace: Trax.Api.Auth.Jwt
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtAuthServiceCollectionExtensions.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtDispatcherBuilder.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtDispatcherRuntime.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/TraxJwtRejectAuthenticationHandler.cs
---

# AddTraxJwtDispatcher

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Extension method on `IServiceCollection`, declared on `JwtAuthServiceCollectionExtensions`, that registers a policy authentication scheme. It reads the `iss` claim of the request's Bearer token and forwards authentication to the JWT scheme, registered with [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth), that the issuer maps to.

## Signature

```csharp fragment
public static AuthenticationBuilder AddTraxJwtDispatcher(
    this IServiceCollection services,
    Action<JwtDispatcherBuilder> configure);
```

```csharp fragment
public sealed class JwtDispatcherBuilder
{
    public JwtDispatcherBuilder MapIssuer(string issuer, string schemeName);
    public JwtDispatcherBuilder WithSchemeName(string schemeName);
    public JwtDispatcherBuilder FallbackToScheme(string schemeName);
}

public sealed class JwtDispatcherRuntime
{
    public IReadOnlyDictionary<string, string> IssuerToScheme { get; }
    public string? ResolveSchemeForToken(string token);
    public ITraxPrincipalResolver<JwtTokenInput>? ResolvePrincipalResolver(string schemeName, IServiceProvider services);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `configure` | `Action<JwtDispatcherBuilder>` | Maps issuers to schemes. At least one `MapIssuer` is required. |

## Members

`JwtDispatcherBuilder`, passed to `configure`:

| Member | Description |
|---|---|
| `MapIssuer(issuer, schemeName)` | Routes tokens whose `iss` is `issuer` to `schemeName`. Mapping an issuer twice throws `InvalidOperationException`. |
| `WithSchemeName(schemeName)` | The dispatcher's own scheme name. Defaults to `JwtDefaults.DispatcherSchemeName`. |
| `FallbackToScheme(schemeName)` | Routes tokens with an unmapped issuer, and requests with no readable token, to `schemeName` instead of the reject scheme. That scheme runs its own issuer validation. |

`JwtDispatcherRuntime`, a singleton the dispatcher registers so the GraphQL socket path can route the same way:

| Member | Description |
|---|---|
| `IssuerToScheme` | The issuer-to-scheme table. |
| `ResolveSchemeForToken(token)` | The scheme the token's `iss` maps to, else the fallback scheme, else `null` (reject). |
| `ResolvePrincipalResolver(schemeName, services)` | The resolver registered for `schemeName`, or `null`. Pass a scoped provider so a scoped resolver resolves. |

## Returns

The `AuthenticationBuilder` from `services.AddAuthentication()`.

## Behavior

On each request the dispatcher:

1. Reads the `Authorization` header. No Bearer token: forward to the fallback.
2. Reads the token's `iss` without validating the signature. Unreadable: forward to the fallback.
3. Looks the issuer up, compared ordinally: case- and trailing-slash-sensitive. A hit forwards to the mapped scheme, a miss to the fallback.

The fallback is the `FallbackToScheme` scheme, or the reject scheme `JwtDefaults.RejectSchemeName`, which fails every request with `No JWT scheme matched the token's issuer.` and answers a challenge with 401. The dispatcher never authenticates a token itself; the scheme it forwards to runs the full `JwtBearer` validation.

| Request | Result |
|---|---|
| No Bearer token, or one whose `iss` cannot be read | The fallback scheme's result: 401 from the reject scheme unless `FallbackToScheme` is set. |
| `iss` not mapped | As above. |
| `iss` mapped, validation succeeds | `Success` from the mapped scheme. |
| `iss` mapped, validation fails | The mapped scheme's failure, 401. |

`AddTraxJwtDispatcher` throws `InvalidOperationException` when no issuer is mapped, or when a scheme with the dispatcher's name is already registered (call `WithSchemeName`).

It registers the policy `{dispatcher scheme}-JwtPolicy` (`TraxJwtDispatcher-JwtPolicy` by default), satisfied by a caller any mapped scheme, or the fallback scheme, authenticated, and adds the dispatcher scheme to `TraxAuthClaimTypes.TraxAuthPolicy`.

### Subscriptions

With a dispatcher registered, JWT socket connections go to `TraxJwtDispatcherSocketInterceptor`. It reads the token from `connection_init`, routes it through `JwtDispatcherRuntime`, and authenticates it with the mapped scheme's own handler, events included. An unmapped issuer is rejected unless `FallbackToScheme` is set. The connection closes when its token expires. See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#authentication).

## Example

```csharp compile
var internalKey = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:InternalKey"]!);

services.AddTraxJwtAuth("customers", jwt =>
    jwt.UseAuthority("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ABC", "mobile-client"));

services.AddTraxJwtAuth("internal", jwt =>
    jwt.UseSymmetricKey("billing-service", "trax-api", internalKey));

services.AddTraxJwtDispatcher(d =>
{
    d.MapIssuer("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ABC", "customers");
    d.MapIssuer("billing-service", "internal");
});

services.AddTraxGraphQL(graphql =>
    graphql.RequireAuthorization(JwtDefaults.DispatcherSchemeName + "-JwtPolicy"));
```

## See also

- [How the JWT dispatcher routes tokens](/docs/security/jwt-issuer-routing)
- [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth), [JwtDefaults](/docs/sdk-reference/api-auth/jwt-defaults), [TraxAuthClaimTypes](/docs/sdk-reference/api-auth/trax-auth-claim-types)
