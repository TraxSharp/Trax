---
layout: default
title: JWT Testing
parent: API Auth
grand_parent: SDK Reference
---

# JWT Testing

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The `Trax.Api.Auth.Jwt.Testing` package ships test fixtures for hosts that integration-test Trax JWT authentication without reaching out to a real identity provider. Reference it from test projects only; never deploy it in production.

## What it provides

| Type | Purpose |
|---|---|
| `TestJwksServer` | In-process Kestrel host serving an OIDC discovery document and JWKS endpoint. Generates an RSA key pair at startup and publishes the public key. |
| `TestTokenIssuer` | Mints signed JWTs (RS256 via `TestJwksServer` or HS256 via `Symmetric`). |
| `TestTokenBuilder` | Fluent builder for individual tokens: subject, claims, roles, audience override, expiry, lifetime. |

## `TestJwksServer`

Start a server, mint a token, send it through your `WebApplicationFactory` host:

```csharp
await using var jwks = await TestJwksServer.StartAsync();

services.AddTraxJwtAuth(jwt =>
    jwt.UseAuthority(jwks.Issuer, "test-aud")
       .AllowHttpMetadata());  // required: TestJwksServer serves HTTP

var token = jwks.CreateIssuer("test-aud").Mint(b =>
    b.WithSubject("alice").WithRole("admin"));

client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", token);
```

The server listens on a random loopback port and disposes cleanly. Each instance generates its own key pair, so two servers in the same process have distinct issuers and `kid`s. Properties:

| Member | Description |
|---|---|
| `Issuer` | `http://127.0.0.1:{port}` — the `iss` claim minted tokens carry. |
| `JwksUri` | `{Issuer}/.well-known/jwks.json` |
| `SigningKey` | The published `RsaSecurityKey` (public + private). |
| `SigningCredentials` | RS256 credentials backed by the same key. |
| `CreateIssuer(audience)` | Returns a `TestTokenIssuer` pre-configured for this server. |

## `TestTokenIssuer`

Mints individual tokens. Construct via `TestJwksServer.CreateIssuer` for RS256, or `TestTokenIssuer.Symmetric` for HS256:

```csharp
// HS256 for internal-token tests:
var keyBytes = Encoding.UTF8.GetBytes(new string('k', 32));
var issuer = TestTokenIssuer.Symmetric("nwyc-web", "nwyc-trax", keyBytes);
var token = issuer.Mint(b =>
    b.WithSubject("alice")
     .WithClaim("subscription_tier", "pro")
     .WithLifetime(TimeSpan.FromMinutes(10)));
```

## `TestTokenBuilder` methods

| Method | Effect |
|---|---|
| `WithSubject(sub)` | Sets the `sub` claim (replaces any previous value). |
| `WithClaim(type, value)` | Appends a claim. Multiple calls with the same type append, do not replace. |
| `WithRole(role)` | Appends a `ClaimTypes.Role` claim. |
| `WithAudience(aud)` | Overrides the audience for this token only. |
| `WithIssuer(iss)` | Overrides the issuer for this token only. |
| `WithNotBefore(dt)` | Sets the `nbf` timestamp explicitly. |
| `WithExpires(dt)` | Sets the `exp` timestamp explicitly. |
| `WithLifetime(timespan)` | Sets `nbf` to one minute ago and `exp` to now + timespan. |

Defaults: `nbf` is one minute ago, `exp` is five minutes from now.

## Test patterns

**Token a real handler would reject** (wrong audience, wrong signature, expired) is what you exercise to prove the negative path:

```csharp
// Token signed with a different key than the JwksServer publishes.
var rogueKey = RSA.Create(2048);
var rogueCreds = new SigningCredentials(new RsaSecurityKey(rogueKey), SecurityAlgorithms.RsaSha256);
var rogueIssuer = new TestTokenIssuer(jwks.Issuer, "test-aud", rogueCreds);
var badToken = rogueIssuer.Mint(b => b.WithSubject("intruder"));

// Server should return 401: signature mismatch.
```

**Expired token**:

```csharp
var token = jwks.CreateIssuer("test-aud").Mint(b =>
    b.WithSubject("alice").WithExpires(DateTime.UtcNow.AddMinutes(-5)));
```

**Mock JWKS rotation**: stand up a new `TestJwksServer` (different `kid`), point the host at it, send a token signed by the new server. Verifies that the framework's JWKS cache refreshes correctly.

## Caveats

- `TestJwksServer` serves over plain HTTP. Tests must call `AllowHttpMetadata()` on the `JwtBuilder` so the bearer handler accepts the non-HTTPS authority.
- The server runs Kestrel in-process. It cannot share a port with another host; each test owning its own server is the expected pattern.
- HS256 keys via `TestTokenIssuer.Symmetric` must be at least 32 bytes (HS256 minimum).

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [UseCognito](/docs/sdk-reference/api-auth/use-cognito) | [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher)
