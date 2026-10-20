---
title: AddTraxPrincipalAccessor
description: Reference for AddTraxPrincipalAccessor, which registers the injectable TraxPrincipal and TraxCaller, and TraxPrincipalNotAvailableException.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 17
package: Trax.Api.Auth
namespace: Trax.Api.Auth
source:
  - Trax.Api/src/Trax.Api.Auth/TraxAuthServiceCollectionExtensions.cs
  - Trax.Api/src/Trax.Api.Auth/TraxPrincipalNotAvailableException.cs
---

# AddTraxPrincipalAccessor

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Extension method on `IServiceCollection`, declared on `TraxAuthServiceCollectionExtensions`, that registers the current request's [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal) and [TraxCaller](/docs/sdk-reference/api-auth/trax-caller) as injectable services. Every `AddTrax*Auth` call runs it.

## Signature

```csharp fragment
public static IServiceCollection AddTraxPrincipalAccessor(this IServiceCollection services);

public sealed class TraxPrincipalNotAvailableException : InvalidOperationException
{
    public TraxPrincipalNotAvailableException();
    public TraxPrincipalNotAvailableException(string message);
}
```

## Returns

The same `IServiceCollection`.

## Behavior

`AddTraxPrincipalAccessor` registers, each with `TryAdd` so the first registration wins and repeated calls do nothing:

| Service | Lifetime | Resolves to |
|---|---|---|
| `TraxPrincipal` | Scoped | The principal read from `HttpContext.User` with `TryGetTraxPrincipal`. Throws `TraxPrincipalNotAvailableException` when there is none. |
| `TraxCaller` | Scoped | The caller context, which reports `null` instead of throwing. |
| `IHttpContextAccessor` | Singleton | |

Within one scope every injection returns the same instance.

### When TraxPrincipal throws

`TraxPrincipalNotAvailableException`, an `InvalidOperationException`, is thrown when `TraxPrincipal` is resolved and:

- the request is anonymous: no scheme produced a principal;
- there is no `HttpContext`: the scheduler, a background service, a test that does not set up the accessor;
- the request's `ClaimsPrincipal` came from a scheme that is not a Trax scheme, so it has no `trax:principal-id` claim.

Its default message reads: `No authenticated Trax principal is available on the current execution context. Injecting TraxPrincipal requires an authenticated HTTP request. For junctions that also run from the scheduler or background services, inject IHttpContextAccessor and probe TryGetTraxPrincipal instead.`

A junction of a train gated by `[TraxAuthorize]` is constructed only after the authorization check passed. Over GraphQL an anonymous caller of such a train gets a `TRAX_AUTHORIZATION` error (`Not authorized.`), not the exception.

### When no scheme is registered

The mediator checks at startup that every junction's constructor arguments resolve, and `TraxPrincipal` resolves only once something registered it. A host whose `AddTrax*Auth` calls are all conditional and none ran refuses to start, naming the junction that needs `Trax.Api.Auth.TraxPrincipal`. Calling `AddTraxPrincipalAccessor()` unconditionally lets it start; every gated train then refuses every caller until a scheme is configured.

## Example

```csharp compile
services.AddAuthentication(); // UseAuthentication() needs it when no scheme was registered
services.AddTraxPrincipalAccessor();

if (builder.Environment.IsDevelopment())
    services.AddTraxApiKeyAuth(keys => keys.Add("member-key-do-not-use-in-production", id: "member", "Member"));
```

## See also

- [Inject the current principal](/docs/security/inject-the-current-principal)
- [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal), [TraxCaller](/docs/sdk-reference/api-auth/trax-caller)
