---
title: IConcurrencyLimiter
description: Reference for IConcurrencyLimiter, which gates direct runs per train, per principal and globally, waiting rather than rejecting when a limit is reached.
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 7
package: Trax.Mediator
namespace: Trax.Mediator.Services.ConcurrencyLimiter
source:
  - Trax.Mediator/src/Trax.Mediator/Services/ConcurrencyLimiter/IConcurrencyLimiter.cs
  - Trax.Mediator/src/Trax.Mediator/Services/ConcurrencyLimiter/ConcurrencyLimiter.cs
---

# IConcurrencyLimiter

The gate behind the [concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting).
`ITrainExecutionService.RunAsync` acquires a permit after authorizing the caller and reading the
input, and releases it when the run ends. Queued work does not pass through it. The limits are
configured on the mediator builder, not through this interface.

## Signature

```csharp fragment
namespace Trax.Mediator.Services.ConcurrencyLimiter;

public interface IConcurrencyLimiter
{
    Task<IDisposable> AcquireAsync(string trainFullName, CancellationToken ct);
}
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `trainFullName` | `string` | The train interface's `FullName`, the canonical train name |
| `ct` | `CancellationToken` | Cancels the wait; throws `OperationCanceledException` without taking a slot |

## Returns

A permit. Disposing it releases every slot it holds; disposing it twice is safe.

## Behavior

- Waits asynchronously while a limit is reached. It never rejects.
- Takes the per-train, per-principal and global limits in that order, so two runs cannot deadlock
  on each other's slots.
- The principal is whatever
  [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider)
  returns at the time of the call.
- A limit that is not configured is skipped.

### Registered services

`AddMediator` registers the default `ConcurrencyLimiter` as a singleton, built from
[MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration), the
`[TraxConcurrencyLimit]` attributes discovery found and the registered `ICurrentPrincipalProvider`.
A replacement registered after `AddMediator` takes its place.

## See also

- [Concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting)
- [Limit concurrent train runs](/docs/mediator/limit-concurrent-train-runs)
- [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)
