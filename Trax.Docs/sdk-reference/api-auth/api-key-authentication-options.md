---
layout: default
title: ApiKeyAuthenticationOptions
parent: API Auth
grand_parent: SDK Reference
---

# ApiKeyAuthenticationOptions

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Options passed to `AddTraxApiKeyAuth` for customizing the scheme. Inherits all standard `AuthenticationSchemeOptions` members.

| Property | Type | Default | Purpose |
|---|---|---|---|
| `HeaderName` | `string` | `"X-Api-Key"` | HTTP header carrying the API key. Override for consumers that use a different convention. |

## Usage

```csharp
services.AddTraxApiKeyAuth<MyResolver>(opts =>
{
    opts.HeaderName = "X-My-Custom-Key";
});
```
