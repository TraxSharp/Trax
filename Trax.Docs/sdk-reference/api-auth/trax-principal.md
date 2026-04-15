---
layout: default
title: TraxPrincipal
parent: API Auth
grand_parent: SDK Reference
---

# TraxPrincipal

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Framework-agnostic identity record produced by an [`ITraxPrincipalResolver`](/docs/sdk-reference/api-auth/i-trax-principal-resolver). Projects to ASP.NET Core's `ClaimsPrincipal` via `TraxPrincipalExtensions.ToClaimsPrincipal(scheme)`.

## Signature

```csharp
public sealed record TraxPrincipal(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyDictionary<string, string>? Claims = null,
    string? PrincipalType = null
);
```

## Fields

| Field | Claim produced | Notes |
|---|---|---|
| `Id` | `trax:principal-id` | Stable identifier. JWT `sub`, account name, Cognito UUID, etc. |
| `DisplayName` | `ClaimTypes.Name` | Human-readable. `HttpContext.User.Identity.Name` returns this. |
| `Roles` | `ClaimTypes.Role` (one per entry) | Consumed by `[TraxAuthorize(Roles = "...")]` and `user.IsInRole(...)`. |
| `Claims` | verbatim (key = type, value = value) | Custom claim bag. Optional. |
| `PrincipalType` | `trax:principal-type` | Scheme discriminator: `apikey`, `jwt`, `cognito`. Optional. |

## Roundtrip

```csharp
var principal = new TraxPrincipal("alice", "Alice", ["User"]);
var claimsPrincipal = principal.ToClaimsPrincipal("TraxApiKey");
// ... request flows through middleware ...
if (claimsPrincipal.TryGetTraxPrincipal(out var roundtripped))
{
    // Same Id, DisplayName, Roles, Claims, PrincipalType
}
```
