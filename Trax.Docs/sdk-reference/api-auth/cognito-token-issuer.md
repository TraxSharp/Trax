---
title: CognitoTokenIssuer
description: "Reference for Trax.Api.Auth.Jwt.Cognito.Issuer: CognitoTokenIssuer, its access and ID token requests and FederatedIdentity, which mint Cognito-shaped tokens."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 9
package: Trax.Api.Auth.Jwt.Cognito.Issuer
namespace: Trax.Api.Auth.Jwt.Cognito.Issuer
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito.Issuer/CognitoTokenIssuer.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito.Issuer/CognitoAccessTokenRequest.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito.Issuer/CognitoIdTokenRequest.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito.Issuer/FederatedIdentity.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito.Issuer/TestJwksServerCognitoExtensions.cs
redirect_from:
  - /docs/sdk-reference/api-auth/cognito-issuer
---

# CognitoTokenIssuer

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Mints RS256 access and ID tokens with the claims Amazon Cognito user pools emit, for a local Cognito stand-in or for tests. It is the minting counterpart of `Trax.Api.Auth.Jwt.Cognito`, which validates such tokens; a host that only validates does not reference this package. Refresh tokens are on [IRefreshTokenStore](/docs/sdk-reference/api-auth/i-refresh-token-store).

## Signature

```csharp fragment
public sealed class CognitoTokenIssuer
{
    public CognitoTokenIssuer(string issuer, RsaSecurityKey signingKey, TimeProvider? clock = null);

    public string MintAccessToken(CognitoAccessTokenRequest request);
    public string MintIdToken(CognitoIdTokenRequest request);
}

public sealed record FederatedIdentity(
    string UserId,
    string ProviderName,
    string ProviderType,
    bool Primary,
    DateTimeOffset DateCreated);

public static class TestJwksServerCognitoExtensions
{
    public static CognitoTokenIssuer CreateCognitoIssuer(this TestJwksServer server, TimeProvider? clock = null);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `issuer` | `string` | The `iss` claim. Real Cognito uses `https://cognito-idp.{region}.amazonaws.com/{userPoolId}`. |
| `signingKey` | `RsaSecurityKey` | The signing key. Its `KeyId` is written to the token header `kid` and must be in the JWKS the validator fetches. A blank `KeyId` throws `ArgumentException`. |
| `clock` | `TimeProvider?` | Sets `iat`, `nbf`, `exp` and the default `auth_time`. Defaults to `TimeProvider.System`. |

## Members

| Member | Description |
|---|---|
| `MintAccessToken(request)` | An access token from a [CognitoAccessTokenRequest](#cognitoaccesstokenrequest). |
| `MintIdToken(request)` | An ID token from a [CognitoIdTokenRequest](#cognitoidtokenrequest). |
| `TestJwksServer.CreateCognitoIssuer(clock)` | An issuer that signs with a [TestJwksServer](/docs/sdk-reference/api-auth/test-jwks-server)'s current key under its issuer URL, so its tokens validate against that server's JWKS. It ships in this package so that `Trax.Api.Auth.Jwt.Testing` does not depend on Cognito issuance. |

### CognitoAccessTokenRequest

| Property | Required | Description |
|---|---|---|
| `Sub` | yes | `Guid`, written as `sub`. |
| `ClientId` | yes | Written as `client_id`. An access token has no `aud`. |
| `Lifetime` | yes | Must be positive. |
| `Username` | no | Written as `username`. Defaults to `Sub`. |
| `Scopes` | no | Joined with spaces into `scope`. Omitted when empty. |
| `Groups` | no | One `cognito:groups` claim each. |
| `AuthTime` | no | Written as `auth_time`. Defaults to the issue time. |
| `Email`, `EmailVerified`, `GivenName`, `FamilyName` | no | Written as `email`, `email_verified`, `given_name`, `family_name` when set. An empty or whitespace `Email` throws. A real pool adds these to access tokens only through a PreTokenGeneration trigger. |
| `AdditionalClaims` | no | `IReadOnlyDictionary<string, string>`, written verbatim. A [reserved name](#reserved-claim-names) throws. |

### CognitoIdTokenRequest

| Property | Required | Description |
|---|---|---|
| `Sub` | yes | `Guid`, written as `sub`. |
| `ClientId` | yes | Written as `aud`. |
| `Lifetime` | yes | Must be positive. |
| `Email` | yes | Written as `email`. Blank throws. |
| `EmailVerified` | yes | Written as `email_verified`. |
| `GivenName`, `FamilyName` | no | Omitted when null or empty. |
| `Username` | no | Written as `cognito:username`. Defaults to `Sub`. |
| `Groups` | no | One `cognito:groups` claim each. |
| `Identities` | no | `FederatedIdentity` entries, written as the `identities` JSON array. |
| `AuthTime` | no | Written as `auth_time`. Defaults to the issue time. |
| `AdditionalClaims` | no | As on the access token request. |

### FederatedIdentity

One entry of the `identities` claim, serialized with camelCase keys.

| Property | Description |
|---|---|
| `UserId` | The provider's subject. |
| `ProviderName` | The Cognito-side provider name (`Google`, `SignInWithApple`). `CognitoJwtPrincipalResolver` surfaces it as `identity_provider`. |
| `ProviderType` | The provider type: the provider name for OIDC federations, `SAML` for SAML. |
| `Primary` | The resolver prefers the primary entry. |
| `DateCreated` | When the identity was linked, serialized as epoch milliseconds. |

## Returns

`MintAccessToken` and `MintIdToken` return the signed token as a compact JWT string.

## Behavior

| Claim | Access token | ID token |
|---|---|---|
| `token_use` | `access` | `id` |
| Audience | `client_id`, no `aud` | `aud` |
| `username` | always | not written |
| `cognito:username` | not written | always |
| `email`, `email_verified` | when set | always |
| `given_name`, `family_name` | when set | when set |
| `identities` | not written | when set |
| `scope` | when `Scopes` is not empty | not written |
| `cognito:groups` | one per group | one per group |
| `auth_time` | `AuthTime`, or the issue time | `AuthTime`, or the issue time |
| `jti` | a new GUID per call | a new GUID per call |

A token minted here and validated through [UseCognito](/docs/sdk-reference/api-auth/use-cognito) and `CognitoJwtPrincipalResolver` gives the `TraxPrincipal` a real Cognito token with the same claims gives. A host that validates the standard `aud` claim (`UseAuthority` rather than `UseCognito`) accepts only the ID token.

### Reserved claim names

`AdditionalClaims` refuses every name the issuer sets itself, and every name `CognitoJwtPrincipalResolver` reads an identity, a display name or roles from. Minting throws `ArgumentException` naming the claim:

`sub`, `iss`, `aud`, `exp`, `nbf`, `iat`, `jti`, `auth_time`, `token_use`, `client_id`, `scope`, `username`, `cognito:username`, `cognito:groups`, `identities`, `email`, `email_verified`, `given_name`, `family_name`, `name`, `preferred_username`, `role`, `roles`, the `ClaimTypes.Role`, `ClaimTypes.Name` and `ClaimTypes.NameIdentifier` URIs, `trax:principal-id` and `trax:principal-type`.

A host that forwards user-editable attributes into `AdditionalClaims` therefore cannot be made to mint a token with an extra group or another subject. A blank name is skipped.

## Examples

```csharp compile
using var rsa = RSA.Create(2048);
var key = new RsaSecurityKey(rsa) { KeyId = "local-pool-key-1" };
var issuer = new CognitoTokenIssuer("http://localhost:8080/local_us-east-1_xxx", key);
var userId = Guid.NewGuid();

var accessToken = issuer.MintAccessToken(new CognitoAccessTokenRequest
{
    Sub = userId,
    ClientId = "mobile-app-client-id",
    Lifetime = TimeSpan.FromHours(1),
    Username = "alice",
    Scopes = ["openid", "profile"],
    Groups = ["admin"],
});

var idToken = issuer.MintIdToken(new CognitoIdTokenRequest
{
    Sub = userId,
    ClientId = "mobile-app-client-id",
    Lifetime = TimeSpan.FromHours(1),
    Email = "alice@example.com",
    EmailVerified = true,
    Identities = [new FederatedIdentity("108222222222", "Google", "Google", Primary: true, DateCreated: DateTimeOffset.UtcNow)],
});
```

Paired with a test JWKS server:

```csharp compile
await using var server = await TestJwksServer.StartAsync();
var issuer = server.CreateCognitoIssuer();

var token = issuer.MintAccessToken(new CognitoAccessTokenRequest
{
    Sub = Guid.NewGuid(),
    ClientId = "test-client",
    Lifetime = TimeSpan.FromHours(1),
});
```

## See also

- [Test JWT authentication](/docs/security/test-jwt-authentication), [Cognito tokens in Trax](/docs/security/cognito-tokens)
- [IRefreshTokenStore](/docs/sdk-reference/api-auth/i-refresh-token-store), [UseCognito](/docs/sdk-reference/api-auth/use-cognito), [TestJwksServer](/docs/sdk-reference/api-auth/test-jwks-server)
