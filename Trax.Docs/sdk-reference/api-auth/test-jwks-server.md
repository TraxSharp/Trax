---
title: TestJwksServer
description: "Reference for Trax.Api.Auth.Jwt.Testing's TestJwksServer and TestJwksServerOptions: an in-process OIDC discovery and JWKS host for JWT tests, with key rotation."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 12
package: Trax.Api.Auth.Jwt.Testing
namespace: Trax.Api.Auth.Jwt.Testing
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt.Testing/TestJwksServer.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Testing/TestJwksServerOptions.cs
redirect_from:
  - /docs/sdk-reference/api-auth/jwt-testing
---

# TestJwksServer

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

An in-process Kestrel host that serves an OIDC discovery document and a JWKS, for tests of Trax JWT authentication that do not reach a real identity provider. It generates an RSA key pair at startup and publishes the public key. The package is for test projects. Tokens are minted with [TestTokenIssuer](/docs/sdk-reference/api-auth/test-token-issuer).

## Signature

```csharp fragment
public sealed class TestJwksServer : IAsyncDisposable
{
    public static Task<TestJwksServer> StartAsync(CancellationToken ct = default);
    public static Task<TestJwksServer> StartAsync(TestJwksServerOptions? options, CancellationToken ct = default);

    public string Issuer { get; }
    public string JwksUri { get; }
    public RsaSecurityKey SigningKey { get; }
    public SigningCredentials SigningCredentials { get; }
    public IReadOnlyList<string> SigningKeyIds { get; }
    public IReadOnlyDictionary<string, SigningCredentials> SigningCredentialsByKid { get; }

    public TestTokenIssuer CreateIssuer(string audience);
    public string AddSigningKey(RSA? rsa = null);
    public bool RemoveSigningKey(string kid);
    public ValueTask DisposeAsync();
}

public sealed record TestJwksServerOptions
{
    public string ListenUrl { get; init; }
    public string? IssuerOverride { get; init; }
    public string PathPrefix { get; init; }
}
```

## Members

| Member | Description |
|---|---|
| `StartAsync(options, ct)` | Starts the host and returns once it listens. |
| `Issuer` | The `iss` minted tokens carry and discovery advertises: `IssuerOverride`, else the bound address followed by `PathPrefix`, such as `http://127.0.0.1:{port}`. |
| `JwksUri` | `{Issuer}/.well-known/jwks.json`. |
| `SigningKey` | The current key, the one added most recently. Throws `InvalidOperationException` when the last key was removed and none added. |
| `SigningCredentials` | RS256 credentials for the current key. |
| `SigningKeyIds` | Every `kid` the JWKS publishes. |
| `SigningCredentialsByKid` | A snapshot of every published credential, keyed by `kid`. |
| `CreateIssuer(audience)` | A [TestTokenIssuer](/docs/sdk-reference/api-auth/test-token-issuer) for this server's issuer and current key, holding every published key so `WithSigningKey(kid)` can switch. |
| `AddSigningKey(rsa)` | Publishes another key, a new 2048-bit one when `rsa` is null, makes it current, and returns its `kid`. The server disposes it. |
| `RemoveSigningKey(kid)` | Removes a key from the JWKS. When it was current, another remaining key becomes current. Returns `false` for an unknown `kid`. |
| `DisposeAsync()` | Stops the host and releases every key. |

`TestJwksServerOptions`:

| Property | Default | Description |
|---|---|---|
| `ListenUrl` | `http://127.0.0.1:0` | The Kestrel bind URL; port 0 picks a free one. |
| `IssuerOverride` | `null` | The `iss` to advertise instead of the bound address, for a reverse proxy or a production-shaped issuer URL. |
| `PathPrefix` | `""` | Serves discovery and the JWKS under this path, such as `/local_us-east-1_xxx`. Non-empty, it must start with `/` and must not end with `/`, or `StartAsync` throws `ArgumentException`. |

## Behavior

- The server serves plain HTTP, so a scheme that validates against it calls `AllowHttpMetadata()` on its `JwtBuilder`.
- Each instance generates its own key pair, so two servers in one process have distinct issuers and key ids.
- A key added while the host runs validates without a restart: `JwtBearerHandler` refreshes its JWKS on an unknown `kid`. A removed key stops validating once the handler refreshes.

## Examples

```csharp compile
await using var jwks = await TestJwksServer.StartAsync();

services.AddTraxJwtAuth(jwt => jwt
    .UseAuthority(jwks.Issuer, "test-aud")
    .AllowHttpMetadata());

var token = jwks.CreateIssuer("test-aud").Mint(b => b.WithSubject("alice").WithRole("admin"));
```

Rotating keys:

```csharp compile
await using var jwks = await TestJwksServer.StartAsync();
var oldKid = jwks.SigningKeyIds.Single();
var newKid = jwks.AddSigningKey();

var issuer = jwks.CreateIssuer("test-aud");
var oldToken = issuer.WithSigningKey(oldKid).Mint(b => b.WithSubject("alice"));
var newToken = issuer.Mint(b => b.WithSubject("bob")); // signed with newKid

jwks.RemoveSigningKey(oldKid); // tokens signed with oldKid now fail validation
```

## See also

- [Test JWT authentication](/docs/security/test-jwt-authentication)
- [TestTokenIssuer](/docs/sdk-reference/api-auth/test-token-issuer), [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer), [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth)
