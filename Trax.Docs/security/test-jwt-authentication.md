---
title: Test JWT authentication
description: How to integration-test a Trax host's JWT authentication with an in-process JWKS server, including rejected tokens, key rotation and Cognito-shaped tokens.
type: how-to
area: security
nav_order: 13
---

# Test JWT authentication

`Trax.Api.Auth.Jwt.Testing` runs an identity provider in the test process, so a test drives the
host's real JWT validation without a network dependency. Reference it, and for Cognito tokens
`Trax.Api.Auth.Jwt.Cognito.Issuer`, from the test project only.

## Point the host at a test JWKS server

Start a [TestJwksServer](/docs/sdk-reference/api-auth/test-jwks-server), register the scheme against
its issuer, and allow HTTP metadata, since the server serves plain HTTP. Then send a minted token:

```csharp compile
using System.Net.Http.Headers;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;

await using var jwks = await TestJwksServer.StartAsync();

services.AddTraxJwtAuth(jwt => jwt
    .UseAuthority(jwks.Issuer, "test-aud")
    .AllowHttpMetadata());

var token = jwks.CreateIssuer("test-aud").Mint(b => b.WithSubject("alice").WithRole("admin"));

using var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
```

Give each test its own server: each binds its own port and generates its own keys.

## Prove the negative paths

A token the host must refuse is minted the same way, with one thing wrong.

Signed with a key the server does not publish:

```csharp compile
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt.Testing;

await using var jwks = await TestJwksServer.StartAsync();

using var rogueKey = RSA.Create(2048);
var rogueCredentials = new SigningCredentials(new RsaSecurityKey(rogueKey), SecurityAlgorithms.RsaSha256);
var forged = new TestTokenIssuer(jwks.Issuer, "test-aud", rogueCredentials).Mint(b => b.WithSubject("intruder"));
// expect 401
```

Expired, or for another audience:

```csharp compile
using Trax.Api.Auth.Jwt.Testing;

await using var jwks = await TestJwksServer.StartAsync();
var issuer = jwks.CreateIssuer("test-aud");

var expired = issuer.Mint(b => b.WithSubject("alice").WithExpires(DateTime.UtcNow.AddMinutes(-5)));
var wrongAudience = issuer.Mint(b => b.WithSubject("alice").WithAudience("another-api"));
// expect 401 for both
```

Keep the expiry well past the host's clock skew, five minutes unless `WithClockSkew` changed it.

## Rotate keys under a running host

Publish a new key with `AddSigningKey()` and mint with it; the bearer handler fetches the JWKS
again on the unknown key id and accepts the token without a restart. Remove the old key with
`RemoveSigningKey(kid)` to check that its tokens stop validating. The calls are on
[TestJwksServer](/docs/sdk-reference/api-auth/test-jwks-server#examples).

## Test with Cognito-shaped tokens

Pair the server with a [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer) and
validate through `UseCognito`, pointed at the test server instead of the real pool:

```csharp compile
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Cognito;
using Trax.Api.Auth.Jwt.Cognito.Issuer;
using Trax.Api.Auth.Jwt.Testing;

await using var server = await TestJwksServer.StartAsync();
var issuer = server.CreateCognitoIssuer();

services.AddTraxJwtAuth<CognitoJwtPrincipalResolver>(jwt =>
{
    jwt.UseCognito("us-east-1", "us-east-1_TestPool", "test-client").AllowHttpMetadata();
    jwt.CustomizeBearerOptions(opts => opts.Authority = server.Issuer);
});

var idToken = issuer.MintIdToken(new CognitoIdTokenRequest
{
    Sub = Guid.NewGuid(),
    ClientId = "test-client",
    Lifetime = TimeSpan.FromHours(1),
    Email = "alice@example.com",
    EmailVerified = true,
    Groups = ["admin"],
    Identities = [new FederatedIdentity("108222222222", "Google", "Google", Primary: true, DateCreated: DateTimeOffset.UtcNow)],
});
```

The resolved principal has `PrincipalType` `cognito`, `Roles` from `cognito:groups`, and
`Claims["identity_provider"]` `Google`.

## SDK Reference

> [TestJwksServer](/docs/sdk-reference/api-auth/test-jwks-server) | [TestTokenIssuer](/docs/sdk-reference/api-auth/test-token-issuer) | [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [UseCognito](/docs/sdk-reference/api-auth/use-cognito)
