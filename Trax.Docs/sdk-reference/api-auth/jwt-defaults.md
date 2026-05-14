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
| `SchemeName` | `TraxJwt` | Default `AuthenticationScheme` name registered by `AddTraxJwtAuth` when no scheme name is supplied. |
| `PolicyName` | `JwtPolicy` | Default authorization policy registered alongside the default scheme. |
| `PrincipalType` | `jwt` | Value written to the `trax:principal-type` claim by `DefaultJwtPrincipalResolver`. |
| `DispatcherSchemeName` | `TraxJwtDispatcher` | Default policy-scheme name registered by `AddTraxJwtDispatcher`. |
| `RejectSchemeName` | `TraxJwtReject` | Internal scheme used by the dispatcher when no issuer mapping matches. Always returns `AuthenticateResult.Fail`. |

Named-scheme registrations derive their authorization policy name from the scheme name: a scheme called `cognito` registers the policy `cognito-JwtPolicy`.
