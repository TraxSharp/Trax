---
layout: default
title: OidcDefaults
parent: API Auth
grand_parent: SDK Reference
---

# OidcDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Constants used by the Trax OpenID Connect scheme.

| Constant | Value | Usage |
|---|---|---|
| `SchemeName` | `TraxOidc` | Challenge scheme. A challenge against this scheme redirects the browser to the identity provider. |
| `CookieSchemeName` | `TraxOidc.Cookie` | Session cookie scheme. Subsequent requests authenticate against this. |
| `PolicyName` | `OidcPolicy` | Authorization policy bound to the cookie scheme. |
| `CallbackPath` | `/signin-oidc` | Default callback path mounted by the OIDC handler. |
| `SignedOutCallbackPath` | `/signout-callback-oidc` | Default sign-out callback path. |
| `PrincipalType` | `oidc` | Value written to the `trax:principal-type` claim when the default resolver builds a `TraxPrincipal`. |
