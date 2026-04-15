---
layout: default
title: ApiKeyDefaults
parent: API Auth
grand_parent: SDK Reference
---

# ApiKeyDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Constants used by the Trax API-key scheme.

| Constant | Value | Usage |
|---|---|---|
| `SchemeName` | `TraxApiKey` | `AuthenticationScheme` name. Pass to `.RequireAuthorization(scheme: ApiKeyDefaults.SchemeName)` when composing with other schemes. |
| `PolicyName` | `ApiKeyPolicy` | Registered authorization policy that requires authenticated user + the API-key scheme. |
| `HeaderName` | `X-Api-Key` | Default request header. Override via [`ApiKeyAuthenticationOptions.HeaderName`](/docs/sdk-reference/api-auth/api-key-authentication-options). |
