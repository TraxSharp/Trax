---
title: IEnqueueContextAccessor
description: Reference for IEnqueueContextAccessor, which exposes the data context an enqueue commits on so an OnQueue hook can write in the same transaction.
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 11
package: Trax.Effect.Data
namespace: Trax.Effect.Data.Services.EnqueueContext
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/EnqueueContext/IEnqueueContextAccessor.cs
---

# IEnqueueContextAccessor

Exposes the data context an enqueue is committing on, so a train's
[`OnQueue`](/docs/mediator/enqueue-hooks) hook can make its side-effect
part of the same transaction as the work queue row. The interface ships in Trax.Effect.Data;
`AddMediator()` registers it, as a singleton, so a train registered with any lifetime may inject it.

## Signature

```csharp fragment
namespace Trax.Effect.Data.Services.EnqueueContext;

public interface IEnqueueContextAccessor
{
    IDataContext? Current { get; }
    IDisposable Enter(IDataContext context);
    IDisposable Suppress();
}
```

## Members

| Member | Description |
|--------|-------------|
| `Current` | The context the enqueue is committing on, or null when no enqueue is in progress on this async flow |
| `Enter(context)` | Makes `context` current for this async flow until the returned scope is disposed, which restores whatever was current before. Called by the enqueue path; consumers read `Current` |
| `Suppress()` | Makes `Current` null for this async flow until the returned scope is disposed, which restores whatever was current before. Called by the enqueue path to hide an outer enqueue's context from a hook that must not join it |

## Behavior

### When Current is set

| Situation | `Current` |
|---|---|
| Inside `OnQueue` for a train that does not defer promotion | The enqueue's context. Writes tracked on it are saved and committed with the work queue row, and rolled back with it if the hook or the insert fails |
| Inside `OnQueue` for a train with `DeferQueuePromotion` | Null. The entry is already committed before the hook runs, or, when the train is enqueued from inside another train's hook, is written in that enqueue's transaction, which the deferring hook does not write into. Null in the nested case from Trax.Mediator 1.23.0; an older Trax.Mediator leaves the outer enqueue's context visible there |
| Anywhere else | Null |

The enqueue path enters a context only for trains that override `OnQueue`.

### Flow and nesting

The value lives in a static `AsyncLocal`, so it follows the async call rather than the accessor
instance or its DI scope. Every instance on the same async flow sees the same value, whichever
scope or lifetime resolved it, including a singleton train's accessor or one resolved from another
scope. Two enqueues running at once on one scope each see their own. An enqueue started from inside
an `OnQueue` hook joins the outer enqueue's transaction (see
[OnQueue](/docs/mediator/enqueue-hooks)), so a nested train that does
not defer promotion sees the outer enqueue's context in its own hook, and what it tracks there
commits or rolls back with the outer entry. Disposing each scope restores what was current before
it. `Suppress()` follows the same rules: it hides the context from the flow that calls it and the
work that flow starts, never from a concurrent enqueue, and disposing it hands back what was there
before.

### What the hook may do with Current

- The enqueue owns the context's lifetime: a hook does not call `SaveChanges` or commit on it.
- It covers only entities in Trax's own model. A hook writing through its own `DbContext` has its
  own connection and transaction and gets no atomicity from this; `DeferQueuePromotion` is the
  setting for that case.

### Infrastructure types

| Type | Description |
|------|-------------|
| `EnqueueContextAccessor` | The implementation `AddMediator()` registers, in `Trax.Effect.Data.Services.EnqueueContext`. The context flows with the async call, not with the instance or its scope |

## Example

The entity is a placeholder for any entity in Trax's model.

```csharp fragment
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

## See also

- [OnQueue](/docs/mediator/enqueue-hooks)
- [ITrainExecutionService.QueueAsync](/docs/sdk-reference/mediator-api/i-train-execution-service#queueasync), the enqueue path
- [IWorkQueuePromotion](/docs/sdk-reference/scheduler-api/i-work-queue-promotion)
