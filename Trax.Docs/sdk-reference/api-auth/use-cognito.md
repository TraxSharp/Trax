---
layout: default
title: UseCognito
parent: API Auth
grand_parent: SDK Reference
---

# UseCognito

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Builder helpers from `Trax.Api.Auth.Jwt.Cognito` that wire `AddTraxJwtAuth` to validate tokens from an Amazon Cognito user pool. Also provides a Cognito-aware `ITraxPrincipalResolver<JwtTokenInput>` that normalizes `cognito:groups`, `cognito:username`, and the `identities` JSON array.

## Signature

```csharp
public static JwtBuilder UseCognito(
    this JwtBuilder builder,
    string region,
    string userPoolId,
    string clientId,
    CognitoTokenUse tokenUse = CognitoTokenUse.IdAndAccess);
```

| Parameter | Purpose |
|---|---|
| `region` | AWS region of the user pool, e.g. `us-east-1`. |
| `userPoolId` | Full user pool identifier, e.g. `us-east-1_AbCdEfGhI`. |
| `clientId` | App client id registered on the pool. Used for both ID-token `aud` and access-token `client_id` validation. |
| `tokenUse` | Which token shapes to accept (`Id`, `Access`, `IdAndAccess`). Defaults to both. |

The helper sets `Authority` to `https://cognito-idp.{region}.amazonaws.com/{userPoolId}` (Cognito's JWKS lives at that URL plus `/.well-known/jwks.json`), the audience to `clientId`, and installs an `AudienceValidator` plus a `LifetimeValidator` that together honor the `tokenUse` selection.

## ID vs access tokens

Cognito issues two token shapes per session:

- **ID tokens** carry user identity claims (`email`, `name`, federated `identities`) and place the app client id in `aud`.
- **Access tokens** carry only OAuth scopes and a `token_use: "access"` claim, and place the app client id in `client_id` (not `aud`).

Many SDKs send access tokens by default. Pick `CognitoTokenUse.IdAndAccess` (default) to accept either, `Access` if your client only ever sends access tokens, or `Id` if you specifically want identity-rich tokens with user attributes.

## Usage

```csharp
services.AddTraxJwtAuth<CognitoJwtPrincipalResolver>("cognito", jwt =>
    jwt.UseCognito(
        region: "us-east-1",
        userPoolId: "us-east-1_AbCdEfGhI",
        clientId: "mobile-app-client-id"));
```

For multi-issuer setups (Cognito plus an internal HS256 scheme, for example), see [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) and [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher).

## `CognitoJwtPrincipalResolver`

A resolver tuned for Cognito's claim shapes. Use it instead of `DefaultJwtPrincipalResolver` when you want Cognito groups, federated provider names, and the username claim normalized for you.

| Token claim | Lands on |
|---|---|
| `sub` (then `nameidentifier`) | `TraxPrincipal.Id` |
| `name`, `preferred_username`, `cognito:username`, `email` (first non-empty) | `TraxPrincipal.DisplayName` |
| `cognito:groups` (repeats) + standard role claims | `TraxPrincipal.Roles` |
| Federated `identities[].providerName` of the `primary: "true"` entry | `Claims["identity_provider"]` |
| Native users (no `identities` claim) | `Claims["identity_provider"] = "cognito"` |
| `token_use`, `email_verified`, scope claims, etc. | `Claims` (verbatim) |
| `TraxPrincipal.PrincipalType` | `cognito` |

The resolver tolerates Apple's second-login behavior (no `email` claim) since `sub` is sufficient to identify the user.

## Federation

Cognito user pools can broker Google, Apple, SAML, and arbitrary OIDC providers. The resulting tokens land in your `AddTraxJwtAuth` handler with the user pool as the issuer, the Cognito-assigned UUID as `sub`, and an `identities` JSON array describing the upstream provider. `CognitoJwtPrincipalResolver` parses the array and exposes the upstream provider name as the synthetic `identity_provider` claim.

Hosts that need a stable user identity across federated providers should configure account linking in Cognito (typically via a `PreSignUp` Lambda trigger that calls `AdminLinkProviderForUser`). The Trax side stays the same: validate the token, look up the user by Cognito `sub`.

## Caveats

- The `LifetimeValidator` installed by `UseCognito` replaces the framework default. If you need additional lifetime checks, chain them via a follow-up `CustomizeTokenValidation` call.
- Audience validation runs through `AudienceValidator`, not the default `ValidAudience`/`ValidAudiences` arrays. Setting `ValidAudiences` manually has no effect on Cognito tokens through this helper.
- `UseCognito` calls `UseAuthority` internally; do not call `UseAuthority`, `UseSymmetricKey`, or `UseSigningKey` on the same builder.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) | [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver)
