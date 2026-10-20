---
title: CognitoDefaults
description: Reference for CognitoDefaults, the Amazon Cognito claim names and values UseCognito, CognitoJwtPrincipalResolver and CognitoTokenIssuer read and write.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 21
package: Trax.Api.Auth.Jwt.Cognito
namespace: Trax.Api.Auth.Jwt.Cognito
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito/CognitoDefaults.cs
---

# CognitoDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

The claim names and values of Amazon Cognito user pool tokens, as [UseCognito](/docs/sdk-reference/api-auth/use-cognito), `CognitoJwtPrincipalResolver` and [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer) use them.

## Signature

```csharp fragment
public static class CognitoDefaults
{
    public const string PrincipalType = "cognito";
    public const string TokenUseId = "id";
    public const string TokenUseAccess = "access";
    public const string CognitoUsername = "cognito:username";
    public const string CognitoGroups = "cognito:groups";
    public const string Identities = "identities";
    public const string IdentityProvider = "identity_provider";
    public const string TokenUse = "token_use";
    public const string ClientId = "client_id";
    public const string EmailVerified = "email_verified";
    public const string Email = "email";
}
```

## Members

| Constant | Value | Description |
|---|---|---|
| `PrincipalType` | `cognito` | The `trax:principal-type` claim `CognitoJwtPrincipalResolver` writes, and its `identity_provider` for a native user. |
| `TokenUse` | `token_use` | The claim that says which token shape this is. |
| `TokenUseId` | `id` | `token_use` on an ID token. |
| `TokenUseAccess` | `access` | `token_use` on an access token. |
| `ClientId` | `client_id` | The claim an access token carries the app client id in. |
| `CognitoUsername` | `cognito:username` | The Cognito username, on ID tokens. |
| `CognitoGroups` | `cognito:groups` | One claim per group the user is in. |
| `Identities` | `identities` | The JSON array of federated identities. |
| `IdentityProvider` | `identity_provider` | Not a Cognito claim: the claim `CognitoJwtPrincipalResolver` adds, naming the federated provider or `cognito`. |
| `Email` | `email` | The user's email. |
| `EmailVerified` | `email_verified` | Whether the email is verified. |

## Example

```csharp compile
app.MapGet("/provider", (ClaimsPrincipal user) =>
    user.FindFirst(CognitoDefaults.IdentityProvider)?.Value ?? CognitoDefaults.PrincipalType);
```

## See also

- [UseCognito](/docs/sdk-reference/api-auth/use-cognito), [Cognito tokens in Trax](/docs/security/cognito-tokens)
