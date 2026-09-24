---
layout: default
title: IEnqueueContextAccessor
parent: Mediator API
grand_parent: SDK Reference
nav_order: 6
---

# IEnqueueContextAccessor

Exposes the data context an enqueue is committing on, so a train's [`OnQueue`](/docs/core/trains-and-junctions#onqueue-enqueue-time-hook) hook can make its side-effect part of the same transaction as the work queue row. Registered as a scoped service by `AddMediator()`.

## Signature

```csharp
namespace Trax.Effect.Data.Services.EnqueueContext;

public interface IEnqueueContextAccessor
{
    IDataContext? Current { get; }
    IDisposable Enter(IDataContext context);
}
```

| Member | Description |
|--------|-------------|
| `Current` | The context the enqueue is committing on, or null when no enqueue is in progress on this async flow |
| `Enter(context)` | Makes `context` current for this async flow until the returned scope is disposed, which restores whatever was current before. Called by the enqueue path; consumers read `Current` |

## When `Current` is set

| Situation | `Current` |
|---|---|
| Inside `OnQueue` for a train that does not defer promotion | The enqueue's context. Writes tracked on it are saved and committed with the work queue row, and rolled back with it if the hook or the insert fails |
| Inside `OnQueue` for a train with `DeferQueuePromotion` | Null. The entry is already committed before the hook runs, so there is no enqueue transaction to join |
| Anywhere else | Null. Fall back to your own context |

The enqueue path enters a context only for trains that override `OnQueue`.

## Flow and nesting

The value lives in a static `AsyncLocal`, so it follows the async call rather than the accessor instance or its DI scope. Every instance on the same async flow sees the same value, whichever scope or lifetime resolved it, including a singleton train's accessor or one resolved from another scope. Two enqueues running at once on one scope each see their own. An enqueue started from inside an `OnQueue` hook gets its own context and transaction for as long as it runs, and disposing its scope restores the outer one.

## Rules for the hook

- Do not call `SaveChanges` or commit on `Current`. The enqueue owns the lifetime.
- It covers only entities in Trax's own model. A hook writing through its own `DbContext` has its own connection and transaction, and gets no atomicity from this; use `DeferQueuePromotion` instead.

## Example

```csharp
public class ReserveSeatTrain(IEnqueueContextAccessor accessor)
    : ServiceTrain<ReserveSeatInput, Unit>, IReserveSeatTrain
{
    protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
    {
        // No SaveChanges: the enqueue commits this with the work queue row.
        await accessor.Current!.Track(new SomeTraxEntity());
    }
}
```

## Package

```
dotnet add package Trax.Effect.Data
```
