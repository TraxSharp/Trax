---
layout: default
title: Injecting TraxPrincipal
description: How to inject the current request's TraxPrincipal into junctions and services, handle resolver failures, and write junctions that also run from the scheduler.
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

`AddTraxPrincipalAccessor()` (called automatically by `AddTraxApiKeyAuth()`, `AddTraxJwtAuth()` and the other Trax auth schemes) registers `TraxPrincipal` as a scoped service with this factory:

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

In practice this should never surprise you: if your junction injects `TraxPrincipal`, gate the upstream endpoint with `[TraxAuthorize]`. The authorization check rejects anonymous callers before the junction is constructed. Over GraphQL that is a `TRAX_AUTHORIZATION` error ("Not authorized.") in the response, not an HTTP 401. If the exception does fire, it's a configuration mistake - you forgot to gate the endpoint.

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

### Ask `TraxCaller`

For code that needs to know *whether* a user is present, inject [`TraxCaller`](/docs/sdk-reference/api-auth/trax-caller).
It never throws: `Principal` is `null` for an anonymous caller, and `IsTrusted` says whether the
scheduler or a runner is executing. It is also the right dependency for an EF query filter or a
subscription resolver.

### Probe via `IHttpContextAccessor`

The same check by hand:

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

## When no scheme is registered

The mediator checks at startup that every junction's constructor arguments can be resolved, and
`TraxPrincipal` resolves only once something has registered it. A host whose auth calls are
conditional (demo keys in Development, configured credentials elsewhere) can end up with no
`AddTrax*Auth` call at all, and then refuses to start:

```
2 of 8 registered trains cannot run:
  - IWhoAmITrain: step 1 (DescribeCallerJunction) needs 'Trax.Api.Auth.TraxPrincipal' as a
    constructor argument; nothing before it puts one in Memory and the container does not
    register it. Register it or chain a junction that produces it first.
```

Register the accessor yourself, unconditionally. The host then starts, and every gated train
refuses every caller until a scheme is configured:

```csharp
using Trax.Api.Auth;

builder.Services.AddAuthentication();          // UseAuthentication() needs it when no scheme ran
builder.Services.AddTraxPrincipalAccessor();
```

## Signature

```csharp
namespace Trax.Api.Auth;

public static class TraxAuthServiceCollectionExtensions
{
    public static IServiceCollection AddTraxPrincipalAccessor(this IServiceCollection services);
}
```

Idempotent. Safe to call from multiple schemes in the same host, and alongside them.

## Package

```
dotnet add package Trax.Api.Auth
```

`Trax.Api.Auth.ApiKey` and `Trax.Api.Auth.Jwt` depend on it, so a host that references either
already has it.
