---
layout: default
title: TraxAuthClaimTypes
description: Reference for TraxAuthClaimTypes, the claim URNs every Trax authentication scheme writes, including the scheme-qualified principal id.
parent: API Auth
grand_parent: SDK Reference
---

# TraxAuthClaimTypes

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Canonical claim URNs used by every Trax authentication scheme.

| Constant | Value | Produced by |
|---|---|---|
| `PrincipalId` | `trax:principal-id` | `TraxPrincipal.Id`, qualified by the scheme: `{scheme}:{id}` |
| `PrincipalType` | `trax:principal-type` | `TraxPrincipal.PrincipalType` (optional) |
| `TraxAuthPolicy` | `TraxAuthPolicy` | Combined authorization policy that accepts any registered Trax auth scheme. |

The id is qualified by the scheme that authenticated it, so the same `sub` from two issuers is two principals: `sub = "abc"` on a JWT scheme named `Customer` is `Customer:abc`. `TraxPrincipalId.Qualify(scheme, id)` computes it; a scheme name containing `:` is refused. See [Qualified Principal Ids](/docs/migration-guides/qualified-principal-ids).

Using a custom URN (`trax:principal-id`) instead of `ClaimTypes.NameIdentifier` avoids collisions with ASP.NET Core Identity. Read via `ClaimsPrincipal.TryGetPrincipalId(out var id)` or `FindFirst(TraxAuthClaimTypes.PrincipalId)`.
