---
title: ICurrentPrincipalProvider
description: Reference for ICurrentPrincipalProvider, which gives the mediator the current caller's stable id for the per-principal concurrency limit on direct runs.
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 8
package: Trax.Mediator
namespace: Trax.Mediator.Services.Principal
source:
  - Trax.Mediator/src/Trax.Mediator/Services/Principal/ICurrentPrincipalProvider.cs
---

# ICurrentPrincipalProvider

Gives the mediator the current caller as a stable id. Its only use in Trax is bucketing the
[per-principal concurrency limit](/docs/sdk-reference/mediator-api/concurrency-limiting) on direct
runs. It is not used for authorization: nothing here grants or checks access.

## Signature

```csharp fragment
namespace Trax.Mediator.Services.Principal;

public interface ICurrentPrincipalProvider
{
    string? GetCurrentPrincipalId();
}
```

## Returns

The current principal's stable id, or `null` when there is none (the scheduler, a remote worker,
an anonymous request).

## Behavior

- A run with a `null` or empty id is not limited per principal; the per-train and global limits
  still apply. Ids are compared ordinally.
- The concurrency limiter is a singleton and resolves the provider once, then calls
  `GetCurrentPrincipalId` on every direct run while the per-principal limit is set.

### Registered services

| Registered by | Implementation |
|---------------|----------------|
| `AddMediator` | A default that always returns `null`, so `PerPrincipalMaxConcurrentRun` limits nothing on its own |
| `AddTraxApi` (called by `AddTraxGraphQL`) | Replaces the default with one that reads the `trax:principal-id` claim of the current request's authenticated user. Called before `AddTrax`, it is replaced by the mediator's default in turn |

## Example

```csharp compile=example
public sealed class JobContextPrincipalProvider : ICurrentPrincipalProvider
{
    public string? GetCurrentPrincipalId() => JobContext.Current?.TenantId;
}
```

```csharp compile=example
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(assembly)
        .PerPrincipalMaxConcurrentRun(5)));

services.AddSingleton<ICurrentPrincipalProvider, JobContextPrincipalProvider>();
```

## See also

- [Write a principal provider](/docs/mediator/write-a-principal-provider)
- [Concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting)
- [IConcurrencyLimiter](/docs/sdk-reference/mediator-api/i-concurrency-limiter)
