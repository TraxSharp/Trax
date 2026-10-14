---
title: TraxAuthClaimTypes
description: Reference for TraxAuthClaimTypes, the claim types every Trax authentication scheme writes, the scheme-qualified principal id, and the combined policy.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 16
package: Trax.Api.Auth
namespace: Trax.Api.Auth
source:
  - Trax.Api/src/Trax.Api.Auth/TraxAuthClaimTypes.cs
---

# TraxAuthClaimTypes

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The claim types every Trax authentication scheme writes, and the name of the policy every scheme joins.

## Signature

```csharp fragment
public static class TraxAuthClaimTypes
{
    public const string PrincipalId = "trax:principal-id";
    public const string PrincipalType = "trax:principal-type";
    public const string TraxAuthPolicy = "TraxAuthPolicy";
}
```

## Members

| Constant | Value | Description |
|---|---|---|
| `PrincipalId` | `trax:principal-id` | Carries `TraxPrincipal.Id` qualified by the scheme that authenticated it: `{scheme}:{id}`. |
| `PrincipalType` | `trax:principal-type` | Carries `TraxPrincipal.PrincipalType` when it is set. |
| `TraxAuthPolicy` | `TraxAuthPolicy` | The combined policy. Each `AddTrax*Auth` call adds its scheme to it, so it accepts a caller from any registered Trax scheme. |

## Behavior

The id is qualified by its scheme, so the same `sub` from two issuers is two principals: `sub = "abc"` on a JWT scheme named `Customer` is `Customer:abc`. [TraxPrincipalId.Qualify](/docs/sdk-reference/api-auth/trax-principal#traxprincipalid) computes it and refuses a scheme name containing `:`.

`trax:principal-id` is a Trax claim type rather than `ClaimTypes.NameIdentifier`, so it does not collide with ASP.NET Core Identity. Read it with `ClaimsPrincipal.TryGetPrincipalId(out var id)` or `FindFirst(TraxAuthClaimTypes.PrincipalId)`.

## Example

```csharp compile
app.MapGet("/me", (ClaimsPrincipal user) =>
        user.TryGetPrincipalId(out var id) ? Results.Ok(id) : Results.Unauthorized())
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);
```

## See also

- [Qualified principal ids](/docs/migration-guides/qualified-principal-ids)
- [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal)
