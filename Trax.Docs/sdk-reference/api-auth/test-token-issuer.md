---
title: TestTokenIssuer
description: Reference for TestTokenIssuer and TestTokenBuilder in Trax.Api.Auth.Jwt.Testing, which mint RS256 or HS256 test JWTs with chosen claims and lifetime.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 13
package: Trax.Api.Auth.Jwt.Testing
namespace: Trax.Api.Auth.Jwt.Testing
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt.Testing/TestTokenIssuer.cs
---

# TestTokenIssuer

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Mints signed JWTs for tests: RS256 through [TestJwksServer.CreateIssuer](/docs/sdk-reference/api-auth/test-jwks-server), HS256 through `Symmetric`, or any `SigningCredentials` through the constructor. `TestTokenBuilder` sets each token's claims.

## Signature

```csharp fragment
public sealed class TestTokenIssuer
{
    public TestTokenIssuer(string issuer, string defaultAudience, SigningCredentials credentials);
    public TestTokenIssuer(string issuer, string defaultAudience, SigningCredentials credentials,
        IReadOnlyDictionary<string, SigningCredentials>? keyset);

    public string Issuer { get; }
    public string DefaultAudience { get; }
    public SigningCredentials Credentials { get; }

    public static TestTokenIssuer Symmetric(string issuer, string audience, byte[] key);
    public TestTokenIssuer WithSigningKey(string kid);
    public string Mint(Action<TestTokenBuilder>? configure = null);
}

public sealed class TestTokenBuilder
{
    public TestTokenBuilder WithSubject(string sub);
    public TestTokenBuilder WithClaim(string type, string value);
    public TestTokenBuilder WithRole(string role);
    public TestTokenBuilder WithAudience(string audience);
    public TestTokenBuilder WithIssuer(string issuer);
    public TestTokenBuilder WithNotBefore(DateTime notBefore);
    public TestTokenBuilder WithExpires(DateTime expires);
    public TestTokenBuilder WithLifetime(TimeSpan lifetime);
}
```

## Members

`TestTokenIssuer`:

| Member | Description |
|---|---|
| `Issuer` | The `iss` of minted tokens. |
| `DefaultAudience` | The `aud` of minted tokens unless `WithAudience` overrides it. |
| `Credentials` | The credentials that sign. |
| `Symmetric(issuer, audience, key)` | An HS256 issuer. Throws `ArgumentException` when `key` is shorter than 32 bytes. |
| `WithSigningKey(kid)` | A new issuer that signs with the keyset's key `kid`. Throws `InvalidOperationException` for an issuer built without a keyset, and `ArgumentException` for an unknown `kid`. |
| `Mint(configure)` | Builds and signs one token. |

`TestTokenBuilder`, passed to `Mint`:

| Member | Effect |
|---|---|
| `WithSubject(sub)` | Sets `sub`, replacing an earlier value. |
| `WithClaim(type, value)` | Appends a claim; repeated calls with one type append. |
| `WithRole(role)` | Appends a `ClaimTypes.Role` claim. |
| `WithAudience(aud)` | Overrides the audience for this token. |
| `WithIssuer(iss)` | Overrides the issuer for this token. |
| `WithNotBefore(dt)` | Sets `nbf`. |
| `WithExpires(dt)` | Sets `exp`. |
| `WithLifetime(timespan)` | Sets `nbf` to one minute ago and `exp` to now plus `timespan`. Throws for a lifetime that is not positive. |

## Returns

`Mint` returns the token as a compact JWT string.

## Behavior

Without `WithNotBefore`, `WithExpires` or `WithLifetime`, a token's `nbf` is one minute ago and its `exp` five minutes from now.

## Example

```csharp compile
var key = Encoding.UTF8.GetBytes(new string('k', 32));
var issuer = TestTokenIssuer.Symmetric("billing-service", "trax-api", key);

var token = issuer.Mint(b => b
    .WithSubject("alice")
    .WithClaim("subscription_tier", "pro")
    .WithLifetime(TimeSpan.FromMinutes(10)));
```

## See also

- [Test JWT authentication](/docs/security/test-jwt-authentication)
- [TestJwksServer](/docs/sdk-reference/api-auth/test-jwks-server), [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth)
