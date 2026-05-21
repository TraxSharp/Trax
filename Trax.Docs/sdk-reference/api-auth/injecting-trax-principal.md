---
layout: default
title: Injecting TraxPrincipal
parent: API Auth
grand_parent: SDK Reference
---

# Injecting TraxPrincipal

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Consumers of authenticated request identity (junctions, application services, minimal API handlers) should inject [`TraxPrincipal`](/docs/sdk-reference/api-auth/trax-principal) directly from DI. The scheme's `Add*` extension registers a scoped factory that resolves the current request's principal with no `IHttpContextAccessor` plumbing in consumer code.

## Usage

```csharp
using Trax.Api.Auth;

public class SendMessageJunction(TraxPrincipal user) : Junction<SendMessageInput, SentMessage>
{
    public override Task<SentMessage> Run(SendMessageInput input) =>
        service.SendAsync(user.Id, user.DisplayName, input.Body);
}
```

That's the whole surface. No `IHttpContextAccessor`, no null checks, no claim lookups. The record has `Id`, `DisplayName`, `Roles`, optional `Claims` bag, optional `PrincipalType`.

## How it works

`AddTraxPrincipalAccessor()` (called automatically by `AddTraxApiKeyAuth()` and every future Trax auth scheme) registers `TraxPrincipal` as a scoped service with this factory:

1. Resolve `IHttpContextAccessor` from DI
2. Read `HttpContext.User` (the `ClaimsPrincipal` populated by the scheme handler)
3. Call `TryGetTraxPrincipal()` to reconstruct the typed record from the claims
4. Return the record, or throw `TraxPrincipalNotAvailableException` if no Trax principal is present

Scoping means every injection within the same request scope returns the same instance. Different requests resolve to different instances.

## When the resolver throws

`TraxPrincipalNotAvailableException` is thrown at DI resolution time whenever:

- The request is anonymous (no auth scheme produced a principal)
- There is no `HttpContext` at all (scheduler path, background service, test code that doesn't set up the accessor)
- The `ClaimsPrincipal` on the request came from a non-Trax scheme (missing the `trax:principal-id` claim)

In practice this should never surprise you: if your junction injects `TraxPrincipal`, gate the upstream endpoint with `[TraxAuthorize]`. The authorization check rejects anonymous callers with a 401 before the junction is constructed. If the exception does fire, it's a configuration mistake - you forgot to gate the endpoint.

## Dual-path junctions (API + scheduler)

If a junction runs from both the API layer AND from the scheduler, constructor injection of `TraxPrincipal` fails on the scheduler path because there is no `HttpContext`. Two recipes:

### Carry the initiator through the input

Preferred for trains that can legitimately run anonymously or on behalf of a queued user:

```csharp
public record AddJobInput(string InitiatorUserId, string JobTitle);

public class AddJobJunction : Junction<AddJobInput, Job>
{
    public override Task<Job> Run(AddJobInput input) =>
        repository.AddAsync(input.InitiatorUserId, input.JobTitle);
}
```

The API-side resolver populates `InitiatorUserId` from the authenticated `TraxPrincipal`; the scheduler replays whatever was persisted on the work queue.

### Probe via `IHttpContextAccessor`

For junctions that need to know *whether* a user is present:

```csharp
public class DualPathJunction(IHttpContextAccessor accessor) : Junction<MyInput, MyOutput>
{
    public override Task<MyOutput> Run(MyInput input)
    {
        var user = accessor.HttpContext?.User;
        if (user?.TryGetTraxPrincipal(out var principal) == true)
        {
            // API path: user-initiated
            return service.RunAsAsync(principal.Id, input);
        }
        // Scheduler path: system-initiated
        return service.RunAsSystemAsync(input);
    }
}
```

## Testing

Tests that construct junctions directly can register a fake principal:

```csharp
services.AddScoped(_ => new TraxPrincipal("test-user", "Test User", ["Admin"]));
```

This overrides the scheme-provided factory because DI picks the last registration. No `HttpContext` mocking required.

## Signature

Registered by [AddTraxPrincipalAccessor](https://github.com/TraxSharp/Trax.Api/blob/main/src/Trax.Api.Auth/TraxAuthServiceCollectionExtensions.cs), called automatically from every Trax auth scheme's setup:

```csharp
public static IServiceCollection AddTraxPrincipalAccessor(this IServiceCollection services);
```

Idempotent. Safe to call from multiple schemes in the same host.
