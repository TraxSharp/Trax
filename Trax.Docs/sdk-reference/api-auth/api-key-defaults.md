---
layout: default
title: ApiKeyDefaults
description: Reference for ApiKeyDefaults, the constants used by the Trax API-key authentication scheme, its scheme name, policy name and the default X-Api-Key header.
parent: API Auth
grand_parent: SDK Reference
---

# ApiKeyDefaults

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Constants used by the Trax API-key scheme.

| Constant | Value | Usage |
|---|---|---|
| `SchemeName` | `TraxApiKey` | `AuthenticationScheme` name. Pass to `.RequireAuthorization(scheme: ApiKeyDefaults.SchemeName)` when composing with other schemes. |
| `PolicyName` | `ApiKeyPolicy` | Registered authorization policy satisfied only by a caller the API-key scheme authenticated, wherever it is evaluated (endpoint gate, `GateOperations`, `[TraxAuthorize(Policy = ...)]`, sockets). |
| `HeaderName` | `X-Api-Key` | Default request header. Override via [`ApiKeyAuthenticationOptions.HeaderName`](/docs/sdk-reference/api-auth/api-key-authentication-options). |
