---
layout: default
title: JwtDefaults
parent: API Auth
grand_parent: SDK Reference
---

# JwtDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Constants used by the Trax JWT bearer scheme.

| Constant | Value | Usage |
|---|---|---|
| `SchemeName` | `TraxJwt` | `AuthenticationScheme` name registered by `AddTraxJwtAuth`. |
| `PolicyName` | `JwtPolicy` | Registered authorization policy that requires authenticated user + the JWT scheme. |
| `PrincipalType` | `jwt` | Value written to the `trax:principal-type` claim when the default resolver builds a `TraxPrincipal`. |
