---
title: JwtDefaults
description: Reference for JwtDefaults, the constants of the Trax JWT bearer scheme and its dispatcher, and how a named scheme derives its authorization policy name.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 14
package: Trax.Api.Auth.Jwt
namespace: Trax.Api.Auth.Jwt
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt/JwtDefaults.cs
---

# JwtDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The constants of the Trax JWT bearer scheme that [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) registers, and of the dispatcher [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher) registers.

## Signature

```csharp fragment
public static class JwtDefaults
{
    public const string SchemeName = "TraxJwt";
    public const string PolicyName = "JwtPolicy";
    public const string PrincipalType = "jwt";
    public const string DispatcherSchemeName = "TraxJwtDispatcher";
    public const string RejectSchemeName = "TraxJwtReject";
}
```

## Members

| Constant | Value | Description |
|---|---|---|
| `SchemeName` | `TraxJwt` | The scheme name `AddTraxJwtAuth` registers when no scheme name is passed. |
| `PolicyName` | `JwtPolicy` | The policy registered with the default scheme, satisfied only by a caller that scheme authenticated. |
| `PrincipalType` | `jwt` | The `trax:principal-type` claim `DefaultJwtPrincipalResolver` writes. |
| `DispatcherSchemeName` | `TraxJwtDispatcher` | The policy scheme `AddTraxJwtDispatcher` registers unless `WithSchemeName` names another. |
| `RejectSchemeName` | `TraxJwtReject` | The scheme the dispatcher forwards to when no mapping matches and no fallback is set. It fails every request, and its challenge is a 401. |

## Behavior

A named scheme's policy is the scheme name followed by `-JwtPolicy`: the scheme `cognito` registers `cognito-JwtPolicy`, and the dispatcher `TraxJwtDispatcher-JwtPolicy`. A named scheme's policy is satisfied only by that scheme; the dispatcher's by any scheme it maps to, or its fallback.

## Example

```csharp compile
app.MapGet("/me", () => Results.Ok()).RequireAuthorization(JwtDefaults.PolicyName);
```

## See also

- [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth), [AddTraxJwtDispatcher](/docs/sdk-reference/api-auth/add-trax-jwt-dispatcher), [TraxAuthClaimTypes](/docs/sdk-reference/api-auth/trax-auth-claim-types)
