---
title: AddTraxJwtAuth
description: "Reference for AddTraxJwtAuth: JWKS authority or explicit signing key, named schemes for several issuers, JwtBuilder methods, the default resolver and demo keys."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 2
package: Trax.Api.Auth.Jwt
namespace: Trax.Api.Auth.Jwt
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtAuthServiceCollectionExtensions.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtBuilder.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/DefaultJwtPrincipalResolver.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtTokenInput.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt/DemoJwtKeyEnvironmentValidator.cs
---

# AddTraxJwtAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Extension methods on `IServiceCollection`, declared on `JwtAuthServiceCollectionExtensions`, that register a Trax JWT bearer authentication scheme. `JwtBearerHandler` validates the token's signature, issuer, audience and lifetime; an `ITraxPrincipalResolver<JwtTokenInput>` then maps the validated token to a `TraxPrincipal`.

## Signatures

```csharp fragment
public static AuthenticationBuilder AddTraxJwtAuth(
    this IServiceCollection services,
    string authority,
    string audience);

public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
    this IServiceCollection services,
    string authority,
    string audience)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;

public static AuthenticationBuilder AddTraxJwtAuth(
    this IServiceCollection services,
    Action<JwtBuilder> configure);

public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
    this IServiceCollection services,
    Action<JwtBuilder> configure)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;

public static AuthenticationBuilder AddTraxJwtAuth(
    this IServiceCollection services,
    string schemeName,
    Action<JwtBuilder> configure);

public static AuthenticationBuilder AddTraxJwtAuth<TResolver>(
    this IServiceCollection services,
    string schemeName,
    Action<JwtBuilder> configure)
    where TResolver : class, ITraxPrincipalResolver<JwtTokenInput>;
```

```csharp fragment
public sealed class JwtBuilder
{
    public JwtBuilder UseAuthority(string authority, string audience);
    public JwtBuilder UseSymmetricKey(string issuer, string audience, byte[] key);
    public JwtBuilder UseSigningKey(string issuer, string audience, SecurityKey key);
    public JwtBuilder WithClockSkew(TimeSpan skew);
    public JwtBuilder AllowHttpMetadata();
    public JwtBuilder CustomizeTokenValidation(Action<TokenValidationParameters> configure);
    public JwtBuilder CustomizeBearerOptions(Action<JwtBearerOptions> configure);
}

public sealed record JwtTokenInput(ClaimsPrincipal Principal, SecurityToken SecurityToken);
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `authority` | `string` | The OIDC issuer URL whose discovery document and JWKS the handler fetches. `AddTraxJwtAuth(authority, audience)` is `AddTraxJwtAuth(jwt => jwt.UseAuthority(authority, audience))`. |
| `audience` | `string` | The expected `aud`. |
| `configure` | `Action<JwtBuilder>` | Configures the scheme. Exactly one key source is required. |
| `schemeName` | `string` | The scheme name. The overloads without it use `JwtDefaults.SchemeName` (`TraxJwt`). |
| `TResolver` | type argument | The resolver for this scheme, registered scoped. Without it the scheme uses a singleton `DefaultJwtPrincipalResolver`. |

## Members

`JwtBuilder`, passed to `configure`:

| Member | Description |
|---|---|
| `UseAuthority(authority, audience)` | Fetch signing keys from the authority's JWKS. The handler caches the keys and refreshes them on an unknown key id. The authority must be HTTPS unless `AllowHttpMetadata()` is called. |
| `UseSymmetricKey(issuer, audience, key)` | An HMAC-SHA256 key. Throws `ArgumentException` when `key` is shorter than 32 bytes. |
| `UseSigningKey(issuer, audience, key)` | Any `SecurityKey`: `RsaSecurityKey`, `ECDsaSecurityKey`, or symmetric. |
| `WithClockSkew(skew)` | Replaces the 5-minute default skew. Throws for a negative value. |
| `AllowHttpMetadata()` | Permits non-HTTPS authority metadata. |
| `CustomizeTokenValidation(configure)` | Edits `TokenValidationParameters` after Trax has set issuer, audience, key and skew. Calls chain in registration order. |
| `CustomizeBearerOptions(configure)` | Edits `JwtBearerOptions` after Trax has wired `OnTokenValidated` to the resolver. Calls chain in registration order. Replacing `Events` wholesale removes the resolver hook. |

`JwtTokenInput`, passed to the resolver:

| Member | Description |
|---|---|
| `Principal` | The `ClaimsPrincipal` `JwtBearerHandler` built from the validated token. |
| `SecurityToken` | The validated token, usually a `JsonWebToken`. |

## Returns

The `AuthenticationBuilder` from `services.AddAuthentication()`, for chaining further schemes.

## Behavior

The scheme validates issuer, audience, lifetime and signing key, and requires a signed token with an expiry. `UseAuthority` and an explicit key (`UseSymmetricKey`, `UseSigningKey`) are exclusive: registering neither or both throws `InvalidOperationException` from `AddTraxJwtAuth`. A null or blank `schemeName` throws `ArgumentException`.

A principal this scheme authenticates carries the id `TraxJwt:{sub}` on the default scheme, or `{schemeName}:{sub}` on a named one. See [Qualified principal ids](/docs/migration-guides/qualified-principal-ids).

### Authentication results

| Request | Result |
|---|---|
| No `Authorization: Bearer` header | `AuthenticateResult.NoResult()` |
| Signature, issuer, audience or lifetime invalid | `JwtBearerHandler` fails it. |
| Valid, resolver returns `null` | `Fail("JWT did not map to a known Trax principal.")` |
| Valid, resolver throws | Fails with the exception. |
| Valid, no resolver registered for the scheme | Fails, naming the scheme. |
| Valid, resolver returns a principal | `Success`, with the principal's claims under the scheme's name. |

### DefaultJwtPrincipalResolver

The resolver a scheme uses when no `TResolver` is given. It returns `null`, failing authentication, when the token has no subject.

| Token claim | Lands on |
|---|---|
| `sub`, then `ClaimTypes.NameIdentifier` | `TraxPrincipal.Id` |
| `name`, then `ClaimTypes.Name`, then `preferred_username`, then the id | `TraxPrincipal.DisplayName`. `email` is not read. |
| `role`, `roles`, `ClaimTypes.Role` | `TraxPrincipal.Roles`, without blanks or duplicates |
| Every other claim | `TraxPrincipal.Claims`, the first value of each type |
| | `TraxPrincipal.PrincipalType` is `jwt`. |

### Demo signing keys start only in Development

Outside Development, the host refuses to start when a JWT bearer scheme validates with a symmetric key whose bytes, read as UTF-8, contain `do-not-use-in-production` (ignoring case). The check reads each bearer scheme's final options at startup, so it finds the key wherever it was set: `UseSymmetricKey`, a `SymmetricSecurityKey` passed to `UseSigningKey`, or an `IssuerSigningKey` or `IssuerSigningKeys` entry set through `CustomizeTokenValidation`, `CustomizeBearerOptions` or a bearer scheme the host registered itself. Keys an `IssuerSigningKeyResolver` returns, or an authority's JWKS serves, exist only when a token is validated and are not checked.

### Policies and default scheme

The default scheme registers `JwtDefaults.PolicyName` (`JwtPolicy`); a named scheme registers `{schemeName}-JwtPolicy`. Each policy is satisfied only by its own scheme. Every scheme joins `TraxAuthClaimTypes.TraxAuthPolicy`. No default authentication scheme is set: the Trax GraphQL endpoint authenticates each request against every registered scheme in registration order and keeps the first that succeeds, while other routes run only the schemes their policy names. A request that carries both an API key and a bearer token is authenticated by whichever scheme was registered first.

### Subscriptions

With `Trax.Api.GraphQL` present and a scheme registered under `JwtDefaults.SchemeName`, JWT socket connections go to `TraxJwtSocketInterceptor`. The token arrives in the `connection_init` payload as `authToken` or `bearer` and is authenticated by the scheme's own `JwtBearerHandler`, as an HTTP request carrying it would be, events included. A rejected connection closes before any operation runs; an accepted one closes when its token expires. Several issuers on one socket need [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher). See [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions#authentication).

### Registered services

| Service | Lifetime |
|---|---|
| Authentication services and a `JwtBearer` scheme under the scheme name | |
| The scheme's resolver: `DefaultJwtPrincipalResolver` or `TResolver`, and on the default scheme `ITraxPrincipalResolver<JwtTokenInput>` | Singleton or scoped |
| The scheme's policy, and the scheme in `TraxAuthPolicy` | |
| [`TraxPrincipal` and `TraxCaller`](/docs/sdk-reference/api-auth/add-trax-principal-accessor), through `AddTraxPrincipalAccessor()` | Scoped |
| The demo-key startup check | Hosted service, registered once |
| The NO WARRANTY startup log | Hosted service, registered once |

## Examples

An authority and audience, with the default resolver:

```csharp compile
services.AddTraxJwtAuth("https://login.example.com", "my-api");
```

An explicit signing key from configuration:

```csharp compile
var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!);

services.AddTraxJwtAuth(jwt => jwt
    .UseSymmetricKey(issuer: "https://trax.internal", audience: "my-api", key: key)
    .WithClockSkew(TimeSpan.FromSeconds(30)));
```

Two issuers side by side, each with its own policy:

```csharp compile
var internalKey = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:InternalKey"]!);

services.AddTraxJwtAuth("customers", jwt =>
    jwt.UseAuthority("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ABC", "mobile-client"));

services.AddTraxJwtAuth("internal", jwt =>
    jwt.UseSymmetricKey("billing-service", "trax-api", internalKey));

app.MapGet("/customers", () => Results.Ok()).RequireAuthorization("customers-JwtPolicy");
app.MapGet("/internal", () => Results.Ok()).RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);
```

## See also

- [JWT bearer authentication](/docs/security/secure-a-graphql-host#1-write-the-host)
- [Write a JWT principal resolver](/docs/security/write-a-jwt-principal-resolver), [Sign development JWTs](/docs/security/sign-development-jwts), [Test JWT authentication](/docs/security/test-jwt-authentication)
- [JwtProviderExtensions](/docs/sdk-reference/api-auth/jwt-provider-extensions), [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher), [UseCognito](/docs/sdk-reference/api-auth/use-cognito), [JwtDefaults](/docs/sdk-reference/api-auth/jwt-defaults)
