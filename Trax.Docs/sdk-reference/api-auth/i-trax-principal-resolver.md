---
layout: default
title: ITraxPrincipalResolver
parent: API Auth
grand_parent: SDK Reference
---

# ITraxPrincipalResolver

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Maps a verified credential input to a [`TraxPrincipal`](/docs/sdk-reference/api-auth/trax-principal). Generic over the input type so the same abstraction serves API keys, validated JWTs, and OIDC claims bags.

## Signature

```csharp
public interface ITraxPrincipalResolver<in TInput>
{
    ValueTask<TraxPrincipal?> ResolveAsync(TInput input, CancellationToken ct);
}
```

## Semantics

- Return a `TraxPrincipal` when the input is valid and maps to a known principal.
- Return `null` when the input is structurally valid but doesn't match anything. The scheme handler surfaces this as an authentication failure.
- Throw only for unexpected errors. The handler catches, logs, and surfaces a failure.

API-key schemes use `ITraxPrincipalResolver<string>` (the raw header value). JWT and OIDC schemes, when added, will use their own input types.

## Lifetime

`AddTraxApiKeyAuth<TResolver>()` registers the resolver as **scoped**. Inject scoped dependencies (DbContext, distributed cache, HTTP client) freely. `AddTraxApiKeyAuth(keys => ...)` registers a singleton `HashedApiKeyResolver` built from the configured entries; the entry set is immutable once built.
