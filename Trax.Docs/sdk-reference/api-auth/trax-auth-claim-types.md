---
layout: default
title: TraxAuthClaimTypes
parent: API Auth
grand_parent: SDK Reference
---

# TraxAuthClaimTypes

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Canonical claim URNs used by every Trax authentication scheme.

| Constant | Value | Produced by |
|---|---|---|
| `PrincipalId` | `trax:principal-id` | `TraxPrincipal.Id` |
| `PrincipalType` | `trax:principal-type` | `TraxPrincipal.PrincipalType` (optional) |
| `TraxAuthPolicy` | `TraxAuthPolicy` | Combined authorization policy that accepts any registered Trax auth scheme. |

Using a custom URN (`trax:principal-id`) instead of `ClaimTypes.NameIdentifier` avoids collisions with ASP.NET Core Identity. Read via `ClaimsPrincipal.TryGetPrincipalId(out var id)` or `FindFirst(TraxAuthClaimTypes.PrincipalId)`.
