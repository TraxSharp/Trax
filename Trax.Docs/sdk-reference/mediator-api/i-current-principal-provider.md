---
layout: default
title: ICurrentPrincipalProvider
description: Reference for ICurrentPrincipalProvider, which gives the mediator the caller's stable id for per-principal concurrency limits, and how to write one.
parent: Mediator API
grand_parent: SDK Reference
nav_order: 8
---

# ICurrentPrincipalProvider

Tells the mediator who the current caller is, as a stable id. Its only use in Trax is bucketing the [per-principal concurrency limit](/docs/sdk-reference/mediator-api/concurrency-limiting#per-principal-limiting) on direct runs. It is not used for authorization: nothing here grants or checks access.

## Signature

```csharp
namespace Trax.Mediator.Services.Principal;

public interface ICurrentPrincipalProvider
{
    string? GetCurrentPrincipalId();
}
```

**Returns**: the current principal's stable id, or `null` when there is none (the scheduler, a remote worker, an anonymous request). A run with a `null` or empty id is not limited per principal; the per-train and global limits still apply. Ids are compared ordinally.

## Registrations

| Registered by | Implementation |
|---------------|----------------|
| `AddMediator` | A default that always returns `null`, so `PerPrincipalMaxConcurrentRun` limits nothing on its own |
| `AddTraxApi` (called by `AddTraxGraphQL`) | Reads the `trax:principal-id` claim of the current request's authenticated user. Call it after `AddTrax`, or the mediator's default wins. |

A host outside ASP.NET Core that wants the per-principal limit registers its own as a singleton after `AddMediator`.

## Writing one

- **Singleton, thread-safe.** The concurrency limiter is a singleton and resolves the provider once. Read ambient state (the current request, an `AsyncLocal`) on each call rather than capturing it.
- **Cheap.** It is called on every direct run while the per-principal limit is set.
- **An id the caller cannot choose.** Use something authenticated, such as the subject claim. An id taken from a header or other client input lets a caller spread its runs across many buckets and escape the limit, because a new id starts with a full budget. Never return a per-request or random value.

## Example

```csharp
using Trax.Mediator.Services.Principal;

public sealed class JobContextPrincipalProvider : ICurrentPrincipalProvider
{
    public string? GetCurrentPrincipalId() => JobContext.Current?.TenantId;
}

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .PerPrincipalMaxConcurrentRun(5)));

services.AddSingleton<ICurrentPrincipalProvider, JobContextPrincipalProvider>();
```

## Package

```
dotnet add package Trax.Mediator
```
