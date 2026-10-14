---
title: IQueuedWorkListener
description: Reference for IQueuedWorkListener, the data provider's notice that work queue entries became dispatchable, which wakes the job dispatcher before its next poll.
type: reference
area: scheduling
parent: Configuration
grand_parent: SDK Reference
nav_order: 43
package: Trax.Effect.Data
namespace: Trax.Effect.Data.Services.QueuedWorkListener
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/QueuedWorkListener/IQueuedWorkListener.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/QueuedWorkListener/IQueuedWorkSubscription.cs
---

# IQueuedWorkListener

The data provider's notice that work queue entries became dispatchable. The scheduler's job dispatcher subscribes to it and starts a cycle when a notice arrives, instead of waiting for its next poll. `UsePostgres` and `UseSqlite` register one; the InMemory provider registers none. `AddScheduler()` consumes it.

## Signature

```csharp fragment
namespace Trax.Effect.Data.Services.QueuedWorkListener;

public interface IQueuedWorkListener
{
    Task<IQueuedWorkSubscription> SubscribeAsync(CancellationToken cancellationToken);
}

public interface IQueuedWorkSubscription : IAsyncDisposable
{
    Task WaitAsync(CancellationToken cancellationToken);
}
```

## Behavior

A subscription hears every notice sent after `SubscribeAsync` returns. `WaitAsync` completes once per notice, and throws when the subscription is lost; the caller disposes it and subscribes again. A notice carries no entry: it says only that something may be ready.

### Providers

| Provider | Heard by | Sent when |
|----------|----------|-----------|
| Postgres | every host on the database | a transaction commits that inserted a `work_queue` row or set a staged row's `confirmed_at`. Migration 069's triggers call `pg_notify('trax_queued_work', '')` inside that transaction, so a rollback sends nothing. Each subscription holds one unpooled connection, named `trax_queued_work_listener` in `pg_stat_activity` |
| SQLite | this process only | a save through a Trax data context of this provider commits a row added as queued, or sets a staged row's `confirmed_at`. A promotion done as a set-based update is not seen |
| InMemory | | no listener is registered |

The poll is always the fallback. Work no notice covers, such as an entry whose `ScheduledAt` comes due, is picked up by the dispatcher's next poll. See [Scheduling](/docs/scheduler).

## See also

- [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [UseSqlite](/docs/sdk-reference/configuration/use-sqlite)
- [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
