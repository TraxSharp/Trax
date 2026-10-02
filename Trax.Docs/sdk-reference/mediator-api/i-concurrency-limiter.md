---
layout: default
title: IConcurrencyLimiter
description: Reference for IConcurrencyLimiter, which gates direct runs per train, per principal and globally, waiting rather than rejecting when a limit is reached.
parent: Mediator API
grand_parent: SDK Reference
nav_order: 9
---

# IConcurrencyLimiter

Gates direct runs per train, per principal and globally. `ITrainExecutionService.RunAsync` acquires a permit after authorizing the caller and reading the input, and releases it when the run ends. Queued work does not pass through it. You configure it through the mediator builder rather than calling it; see [Concurrency Limiting](/docs/sdk-reference/mediator-api/concurrency-limiting).

## Signature

```csharp
namespace Trax.Mediator.Services.ConcurrencyLimiter;

public interface IConcurrencyLimiter
{
    Task<IDisposable> AcquireAsync(string trainFullName, CancellationToken ct);
}
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `trainFullName` | `string` | The train interface's `FullName`, the canonical train name |
| `ct` | `CancellationToken` | Cancels the wait; throws `OperationCanceledException` without taking a slot |

**Returns**: a permit. Disposing it releases every slot it holds; disposing it twice is safe.

## Behavior

- Waits asynchronously while a limit is reached. It never rejects.
- Takes the per-train, per-principal and global limits in that order, so two runs cannot deadlock on each other's slots.
- The principal is whatever [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider) returns at the time of the call.
- A limit that is not configured is skipped.

`AddMediator` registers the default `ConcurrencyLimiter` as a singleton, built from [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration) and the `[TraxConcurrencyLimit]` attributes discovery found. Replace it only to change how limits are enforced, for example to share them across processes; register yours after `AddMediator`.

## Package

```
dotnet add package Trax.Mediator
```
