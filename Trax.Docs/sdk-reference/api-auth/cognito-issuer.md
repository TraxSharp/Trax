---
layout: default
title: Cognito Issuer
parent: API Auth
grand_parent: SDK Reference
---

# Cognito Issuer

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`Trax.Api.Auth.Jwt.Cognito.Issuer` is the symmetric counterpart to `Trax.Api.Auth.Jwt.Cognito`. The cognito package validates Cognito-shaped tokens; this package mints them. Use it in local-development stand-ins for Cognito (a Docker service that speaks the user-pool dialect to NextAuth, Amplify, or any OIDC client) and in integration tests that need real Cognito claim shapes rather than the generic JWTs `TestTokenIssuer` emits.

The package is consumed in addition to, not instead of, the validator. Server code that only validates tokens keeps referencing `Trax.Api.Auth.Jwt.Cognito` and never pulls in the issuer.

## What it provides

| Type | Purpose |
|---|---|
| `CognitoTokenIssuer` | Mints RS256 access and ID tokens with Cognito claim conventions. |
| `CognitoAccessTokenRequest`, `CognitoIdTokenRequest` | Inputs for mint calls. |
| `FederatedIdentity` | One entry in the Cognito `identities` claim. |
| `IRefreshTokenStore` | Persistence contract for opaque refresh tokens with rotation chains. |
| `InMemoryRefreshTokenStore` | Process-memory implementation for tests and local dev. |
| `TestJwksServer.CreateCognitoIssuer()` | Extension that pairs a `TestJwksServer` with a `CognitoTokenIssuer`. |

## `CognitoTokenIssuer`

```csharp
using var rsa = RSA.Create(2048);
var key = new RsaSecurityKey(rsa) { KeyId = "local-pool-key-1" };
var issuer = new CognitoTokenIssuer(
    issuer: "http://localhost:8080/local_us-east-1_xxx",
    signingKey: key);

var accessToken = issuer.MintAccessToken(new CognitoAccessTokenRequest
{
    Sub = userId,
    ClientId = "mobile-app-client-id",
    Lifetime = TimeSpan.FromHours(1),
    Username = "alice",
    Scopes = new[] { "openid", "profile" },
    Groups = new[] { "admin" },
});

var idToken = issuer.MintIdToken(new CognitoIdTokenRequest
{
    Sub = userId,
    ClientId = "mobile-app-client-id",
    Lifetime = TimeSpan.FromHours(1),
    Email = "alice@example.com",
    EmailVerified = true,
    GivenName = "Alice",
    FamilyName = "Smith",
});
```

The signing key must have `KeyId` set; the same value lands in the JWT header `kid` and must be discoverable in whatever JWKS your validator fetches. Inject a `TimeProvider` to control `iat`/`exp` in tests.

### Access vs ID token claim shape

| Claim | Access token | ID token |
|---|---|---|
| `token_use` | `"access"` | `"id"` |
| Audience | `client_id` claim (no `aud`) | `aud` claim |
| `username` | always | (uses `cognito:username`) |
| `cognito:username` | omitted | always |
| `email`, `email_verified` | omitted | required |
| `given_name`, `family_name` | omitted | optional |
| `identities` | omitted | optional, JSON-array string |
| `scope` | space-delimited from `Scopes` | omitted |
| `cognito:groups` | repeated per entry | repeated per entry |
| `auth_time` | request `AuthTime` or `iat` | request `AuthTime` or `iat` |
| `jti` | fresh GUID per call | fresh GUID per call |

`AuthTime` lets refresh-issued tokens carry the original sign-in time even though `iat` reflects the rotation. Leave it null for a fresh login.

### `CognitoAccessTokenRequest`

| Property | Required | Notes |
|---|---|---|
| `Sub` | yes | Cognito uses a GUID per user. |
| `ClientId` | yes | App client id. Written as `client_id`, not `aud`. |
| `Lifetime` | yes | Must be positive. |
| `Username` | no | Defaults to `Sub.ToString()`. |
| `Scopes` | no | Joined into a space-delimited `scope` claim; omitted when empty. |
| `Groups` | no | Each entry becomes a repeated `cognito:groups` claim. |
| `AuthTime` | no | Original authentication time; defaults to the issuer clock. |
| `AdditionalClaims` | no | String-valued claims appended verbatim (use for `custom:*` attributes). |

### `CognitoIdTokenRequest`

| Property | Required | Notes |
|---|---|---|
| `Sub` | yes | Same as access token. |
| `ClientId` | yes | Written as `aud`. |
| `Lifetime` | yes | Must be positive. |
| `Email` | yes | OIDC profile claim. |
| `EmailVerified` | yes | OIDC profile claim. |
| `GivenName`, `FamilyName` | no | Omitted when null or empty. |
| `Username` | no | Defaults to `Sub.ToString()`. Lands in `cognito:username`. |
| `Groups` | no | Repeated `cognito:groups`. |
| `Identities` | no | Federated identities; see below. |
| `AuthTime` | no | Original authentication time. |
| `AdditionalClaims` | no | String-valued extras. |

### `FederatedIdentity`

One entry per linked external provider. Cognito serializes the list as a JSON-array string under the `identities` claim, which `CognitoJwtPrincipalResolver` parses to surface `identity_provider`.

| Field | Notes |
|---|---|
| `UserId` | Provider-issued subject (Google numeric id, Apple opaque sub, SAML NameID). |
| `ProviderName` | Cognito-side provider name (`Google`, `SignInWithApple`, etc.). |
| `ProviderType` | Discriminator. Matches `ProviderName` for OIDC federations; `SAML` for SAML. |
| `Primary` | Cognito's resolver prefers the primary entry when surfacing `identity_provider`. |
| `DateCreated` | When the identity was linked; serialized as epoch milliseconds. |

## Refresh tokens

Real Cognito refresh tokens are opaque blobs. The `IRefreshTokenStore` contract captures the four operations a Cognito-compatible auth server has to support.

| Method | Behavior |
|---|---|
| `IssueAsync(sub, clientId, lifetime, ct)` | Start a new rotation chain. |
| `ValidateAsync(token, ct)` | Returns claims if active; null if expired, revoked, consumed, or unknown. |
| `RotateAsync(oldToken, ct)` | Atomically consume one token and issue a successor in the same chain. Returns null when the supplied token is invalid. Rotation does not extend session length, the new token inherits the original expiry. |
| `RevokeAsync(token, ct)` | Revokes the entire rotation chain the token belongs to. |
| `RevokeAllAsync(sub, clientId, ct)` | Global sign-out: revoke every chain for the user. |

`InMemoryRefreshTokenStore` is provided for tests and local-dev auth servers. It generates 256-bit random tokens, holds state in process memory, and is not safe across restarts. Production hosts implement `IRefreshTokenStore` against their own persistence (a relational table keyed by token hash, a Redis namespace with TTLs, etc.).

```csharp
var store = new InMemoryRefreshTokenStore();

var refresh = await store.IssueAsync(userId, clientId, TimeSpan.FromDays(30), ct);

// Later, the client refreshes:
var next = await store.RotateAsync(refresh.Token, ct);
if (next is null)
{
    // The token was already rotated, revoked, or expired. Force re-auth.
}
```

Concurrent rotations of the same token are race-safe: exactly one caller wins, every other concurrent attempt gets null.

## `TestJwksServer.CreateCognitoIssuer()`

Pairs a [test JWKS server](/docs/sdk-reference/api-auth/jwt-testing) with a `CognitoTokenIssuer` that signs against its current key and uses its issuer URL. Tokens validate against the same server's JWKS without further setup.

```csharp
await using var server = await TestJwksServer.StartAsync();
var issuer = server.CreateCognitoIssuer();

var token = issuer.MintAccessToken(new CognitoAccessTokenRequest
{
    Sub = userId,
    ClientId = "test-client",
    Lifetime = TimeSpan.FromHours(1),
});
```

The extension lives in this package, not in `Trax.Api.Auth.Jwt.Testing`, so consumers of the testing package don't pull in Cognito issuance transitively.

## End-to-end round trip

A token minted here validates through [`UseCognito`](/docs/sdk-reference/api-auth/use-cognito) into a `TraxPrincipal` with the same shape a real Cognito-issued token produces.

```csharp
await using var server = await TestJwksServer.StartAsync();
var issuer = server.CreateCognitoIssuer();

services.AddTraxJwtAuth<CognitoJwtPrincipalResolver>(jwt =>
{
    jwt.UseCognito("us-east-1", "us-east-1_TestPool", "test-client").AllowHttpMetadata();
    jwt.CustomizeBearerOptions(opts =>
    {
        opts.Authority = server.Issuer;
        opts.RequireHttpsMetadata = false;
    });
});

var idToken = issuer.MintIdToken(new CognitoIdTokenRequest
{
    Sub = userId,
    ClientId = "test-client",
    Lifetime = TimeSpan.FromHours(1),
    Email = "alice@example.com",
    EmailVerified = true,
    Identities = new[]
    {
        new FederatedIdentity("108222222222", "Google", "Google", Primary: true, DateCreated: DateTimeOffset.UtcNow),
    },
});
```

After validation the resolved principal has `PrincipalType = "cognito"`, `Roles` populated from `cognito:groups`, and a `Claims["identity_provider"] = "Google"` entry from the `identities` array.

## Caveats

- The issuer matches the real-Cognito wire format, but Trax does not ship the rest of the Cognito API surface (`SignUp`, `InitiateAuth`, hosted UI, MFA, Lambda triggers). Hosts that need a full local-Cognito service build it on top of these primitives. See the package's README for context.
- `AdditionalClaims` is `IReadOnlyDictionary<string, string>`. Cognito custom attributes that need non-string types (arrays, booleans, nested objects) are not expressible through this API.
- The signing-key `KeyId` is required. Missing it would produce tokens whose `kid` header is empty, breaking JWKS lookup at the validator.

## SDK Reference

> [UseCognito](/docs/sdk-reference/api-auth/use-cognito) | [JWT Testing](/docs/sdk-reference/api-auth/jwt-testing) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth)
