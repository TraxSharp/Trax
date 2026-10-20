---
title: TraxPrincipal
description: "Reference for TraxPrincipal, the identity record a resolver returns: its properties, the ClaimsPrincipal round trip and TraxPrincipalId."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 19
package: Trax.Api.Auth
namespace: Trax.Api.Auth
source:
  - Trax.Api/src/Trax.Api.Auth/TraxPrincipal.cs
  - Trax.Api/src/Trax.Api.Auth/TraxPrincipalExtensions.cs
  - Trax.Api/src/Trax.Api.Auth/TraxPrincipalId.cs
---

# TraxPrincipal

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

The identity record an [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver) returns, independent of any authentication framework. `TraxPrincipalExtensions` projects it to an ASP.NET Core `ClaimsPrincipal` and back; `TraxPrincipalId` builds the scheme-qualified id the claims carry.

## Signature

```csharp fragment
public record TraxPrincipal(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyDictionary<string, string>? Claims = null,
    string? PrincipalType = null
);

public static class TraxPrincipalExtensions
{
    public static ClaimsPrincipal ToClaimsPrincipal(this TraxPrincipal principal, string authenticationType);
    public static bool TryGetTraxPrincipal(this ClaimsPrincipal claimsPrincipal, out TraxPrincipal? principal);
    public static bool TryGetPrincipalId(this ClaimsPrincipal claimsPrincipal, out string? id);
}

public static class TraxPrincipalId
{
    public const char Separator = ':';
    public static string Qualify(string scheme, string id);
}
```

## Properties

| Property | Claim produced | Description |
|---|---|---|
| `Id` | `trax:principal-id` | Stable within its scheme: a JWT `sub`, an account name, a Cognito UUID. The claim carries it qualified by the scheme, `{scheme}:{Id}`. |
| `DisplayName` | `ClaimTypes.Name` | Human-readable. `HttpContext.User.Identity.Name` returns it. |
| `Roles` | `ClaimTypes.Role`, one per entry | Read by `[TraxAuthorize(Roles = "...")]` and `IsInRole(...)`. |
| `Claims` | one claim per entry, type and value as given | Optional. An entry whose type is `trax:principal-id`, `trax:principal-type`, `ClaimTypes.Name` or `ClaimTypes.Role` is not written. |
| `PrincipalType` | `trax:principal-type` | Optional. The scheme's discriminator: `apikey`, `jwt`, `cognito`, `oidc`. |

## Behavior

`Id` and `DisplayName` must not be null, empty or whitespace: the constructor and `with` expressions throw `ArgumentException`.

### TraxPrincipalExtensions

| Method | Description |
|---|---|
| `ToClaimsPrincipal(authenticationType)` | One `ClaimsIdentity` of type `authenticationType` holding the claims above, the id qualified by `authenticationType`. Throws for an empty `authenticationType` or one containing `:`. |
| `TryGetTraxPrincipal(out principal)` | Reads a principal back. `false` when there is no `trax:principal-id` claim. The id read back is the qualified one; `DisplayName` falls back to it when there is no name claim. |
| `TryGetPrincipalId(out id)` | The qualified id alone. |

A principal read back carries `{scheme}:{id}`, so projecting it again qualifies it twice: project a resolver's output once.

A claim type can appear more than once in a `ClaimsPrincipal`, for instance after an `IClaimsTransformation` that adds one claim per permission, or under a policy naming several schemes, whose evaluator merges each scheme's identity into one principal. `TryGetTraxPrincipal` keeps the first value of each type, the one `FindFirst` returns, so a merged principal reads as its first identity. `FindAll(type)` returns every value.

### TraxPrincipalId

`Qualify(scheme, id)` returns `{scheme}:{id}`, the id a principal authenticated by `scheme` carries, for seeding or migrating rows keyed on it. It throws `ArgumentException` for an empty scheme, a scheme containing `:`, or a blank id.

## Example

```csharp compile
var principal = new TraxPrincipal("alice", "Alice", ["User"], PrincipalType: "apikey");
var claimsPrincipal = principal.ToClaimsPrincipal(ApiKeyDefaults.SchemeName);

if (claimsPrincipal.TryGetTraxPrincipal(out var readBack))
    Console.WriteLine(readBack.Id); // TraxApiKey:alice

var seeded = TraxPrincipalId.Qualify(ApiKeyDefaults.SchemeName, "alice"); // TraxApiKey:alice
```

## See also

- [Inject the current principal](/docs/security/inject-the-current-principal)
- [Qualified principal ids](/docs/migration-guides/qualified-principal-ids)
- [TraxCaller](/docs/sdk-reference/api-auth/trax-caller), [AddTraxPrincipalAccessor](/docs/sdk-reference/api-auth/add-trax-principal-accessor), [TraxAuthClaimTypes](/docs/sdk-reference/api-auth/trax-auth-claim-types)
