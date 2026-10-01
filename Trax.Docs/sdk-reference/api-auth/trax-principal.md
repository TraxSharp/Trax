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
| `Id` | `trax:principal-id` | Stable identifier within its scheme: JWT `sub`, account name, Cognito UUID, etc. The claim carries it qualified by the scheme, `{scheme}:{Id}`. |
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
    // roundtripped.Id is "TraxApiKey:alice"; DisplayName, Roles, Claims, PrincipalType unchanged
}
```

`ToClaimsPrincipal(scheme)` qualifies the id by the scheme, so a principal read back from the
claims (`TryGetTraxPrincipal`, the injected `TraxPrincipal`, `TraxCaller.Principal`) carries
`{scheme}:{id}`. Project a resolver's output once: projecting a read-back principal again
qualifies it twice.

A claim type can appear more than once in a `ClaimsPrincipal`: an `IClaimsTransformation` that adds
one claim per permission, or a policy naming several schemes, whose evaluator merges each scheme's
identity into one principal. Reading it back keeps the first value of each type, the one
`ClaimsPrincipal.FindFirst` returns, so a merged principal reads as its first identity. The `Claims`
bag holds one value per type; read `claimsPrincipal.FindAll(type)` for every value of a multi-valued
claim.

## TraxPrincipalId

```csharp
public static class TraxPrincipalId
{
    public const char Separator = ':';
    public static string Qualify(string scheme, string id);
}
```

`Qualify` returns the id a principal authenticated by `scheme` carries, for seeding or migrating
rows keyed on it. It throws `ArgumentException` for an empty scheme, a scheme containing `:`, or
an empty id. See [Qualified Principal Ids](/docs/migration-guides/qualified-principal-ids).
