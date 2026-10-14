---
title: Write a principal provider
description: "Write an ICurrentPrincipalProvider so the per-principal concurrency limit applies on a host that is not a Trax GraphQL host, and register it."
type: how-to
area: mediator
nav_order: 21
---

# Write a principal provider

`PerPrincipalMaxConcurrentRun` gives each caller its own budget of concurrent direct runs. The
mediator learns who the caller is from `ICurrentPrincipalProvider`. A GraphQL host gets one that
reads the authenticated user from `AddTraxApi`; any other host (a worker, a gRPC service, a console
host) writes its own, or the limit holds nothing.

## 1. Pick the id

Use something the caller cannot choose: the subject claim of an authenticated user, a tenant id
your own authentication set. An id taken from a header or other client input lets a caller spread
its runs across as many ids as it likes, each starting with a full budget. Never return a
per-request or random value. Return `null` for work with no caller, such as a background job; it is
not limited per principal.

## 2. Write the provider

The concurrency limiter is a singleton and resolves the provider once, so the provider is a
singleton too and must be thread-safe. Read the ambient state (the current request, an
`AsyncLocal`) on every call rather than capturing it in the constructor. Keep it cheap: it runs on
every direct run while the limit is set.

```csharp compile=app
using Trax.Mediator.Services.Principal;

public sealed class TenantPrincipalProvider : ICurrentPrincipalProvider
{
    public string? GetCurrentPrincipalId() => TenantScope.CurrentTenantId;
}
```

## 3. Register it after AddMediator

`AddMediator` registers a default that returns `null`. Register yours after it, so yours is the one
resolved:

```csharp compile=app
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.Principal;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(TenantPrincipalProvider).Assembly)
        .PerPrincipalMaxConcurrentRun(5)));

services.AddSingleton<ICurrentPrincipalProvider, TenantPrincipalProvider>();
```

## SDK Reference

> [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider) | [Concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting)
