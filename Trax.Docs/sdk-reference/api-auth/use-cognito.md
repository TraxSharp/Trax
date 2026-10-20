---
title: UseCognito
description: Reference for UseCognito, which configures a JwtBuilder for an Amazon Cognito user pool, and CognitoJwtPrincipalResolver for groups and federation.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 20
package: Trax.Api.Auth.Jwt.Cognito
namespace: Trax.Api.Auth.Jwt.Cognito
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito/CognitoJwtBuilderExtensions.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito/CognitoTokenUse.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito/CognitoJwtPrincipalResolver.cs
---

# UseCognito

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Extension method on `JwtBuilder`, declared on `CognitoJwtBuilderExtensions`, that configures an [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) scheme to validate tokens from an Amazon Cognito user pool. The package also ships `CognitoJwtPrincipalResolver`, a resolver for Cognito's claim shapes.

## Signature

```csharp fragment
public static JwtBuilder UseCognito(
    this JwtBuilder builder,
    string region,
    string userPoolId,
    string clientId,
    CognitoTokenUse tokenUse = CognitoTokenUse.IdAndAccess);

public enum CognitoTokenUse
{
    Id,
    Access,
    IdAndAccess,
}

public sealed class CognitoJwtPrincipalResolver : ITraxPrincipalResolver<JwtTokenInput>
{
    public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `region` | `string` | The user pool's AWS region, such as `us-east-1`. |
| `userPoolId` | `string` | The user pool id, such as `us-east-1_AbCdEfGhI`. |
| `clientId` | `string` | The app client id. An ID token must carry it in `aud`, an access token in `client_id`. |
| `tokenUse` | `CognitoTokenUse` | Which token shapes to accept: `Id`, `Access`, or `IdAndAccess` (the default). |

## Returns

The same `JwtBuilder`, for chaining.

## Behavior

`UseCognito` calls `UseAuthority("https://cognito-idp.{region}.amazonaws.com/{userPoolId}", clientId)`, so it cannot be combined with `UseSymmetricKey` or `UseSigningKey` on the same builder. It then calls `CustomizeTokenValidation` to install two validators:

- **`AudienceValidator`**: accepts a token whose `aud` contains `clientId` when `tokenUse` admits ID tokens, and a token whose `client_id` claim is `clientId` when it admits access tokens. `ValidAudience` and `ValidAudiences` are not consulted.
- **`LifetimeValidator`**: runs any lifetime validator set earlier on the builder, then checks `token_use`. It refuses an `id` token when only `Access` is admitted, and an `access` token when only `Id` is. Where access tokens are admitted, a token that carries `client_id` but no `token_use` is refused. A token that carries neither claim is not affected. It then checks `exp`, which is required, and `nbf` against the clock skew.

A later `CustomizeTokenValidation` call that assigns `AudienceValidator` or `LifetimeValidator` replaces the one `UseCognito` installed, and with it these checks.

A null or blank `region`, `userPoolId` or `clientId` throws `ArgumentException`.

### CognitoJwtPrincipalResolver

Returns `null`, failing authentication, when the token has no subject.

| Token claim | Lands on |
|---|---|
| `sub`, then `ClaimTypes.NameIdentifier` | `TraxPrincipal.Id` |
| The first non-empty of `name`, `ClaimTypes.Name`, `preferred_username`, `cognito:username`, `email`, `ClaimTypes.Email`, then the id | `TraxPrincipal.DisplayName` |
| `cognito:groups`, `role`, `roles`, `ClaimTypes.Role` | `TraxPrincipal.Roles`, without blanks or duplicates |
| `identities`: the `providerName` of the entry with `primary` true, else of the first entry | `Claims["identity_provider"]` |
| No `identities`, or one that cannot be read | `Claims["identity_provider"] = "cognito"` |
| Every other claim (`token_use`, `email`, `email_verified`, scopes) | `TraxPrincipal.Claims`, the first value of each type |
| | `TraxPrincipal.PrincipalType` is `cognito`. |

A token with no `email`, such as Apple's after the first sign-in, still resolves: `sub` identifies the user.

## Example

```csharp compile
services.AddTraxJwtAuth<CognitoJwtPrincipalResolver>("cognito", jwt =>
    jwt.UseCognito(
        region: "us-east-1",
        userPoolId: "us-east-1_AbCdEfGhI",
        clientId: "mobile-app-client-id"));
```

## See also

- [Cognito tokens in Trax](/docs/security/cognito-tokens): ID and access tokens, federation, local development.
- [CognitoDefaults](/docs/sdk-reference/api-auth/cognito-defaults), [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer)
- [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth), [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher)
