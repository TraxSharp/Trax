---
layout: default
title: IWorkQueuePromotion
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 13
---

# IWorkQueuePromotion

Confirms work queue entries staged by a two-phase enqueue, and resolves the ones a crash left unconfirmed. A train with [`DeferQueuePromotion`](/docs/core/trains-and-junctions#making-the-side-effect-durable) has its entry committed unconfirmed, runs `OnQueue`, and is then promoted; the dispatcher never claims an unconfirmed entry.

Registered as a singleton by `AddMediator()`; it creates a data context per call. The enqueue path and the ManifestManager call it; you rarely need to.

## Signature

```csharp
namespace Trax.Effect.Data.Services.WorkQueuePromotion;

public interface IWorkQueuePromotion
{
    Task<bool> PromoteAsync(long workQueueId, CancellationToken cancellationToken);
    Task<int> CancelStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken);
    Task<int> PromoteStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken);
}
```

## Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `PromoteAsync(workQueueId, ct)` | `Task<bool>` | Marks one queued, unconfirmed entry dispatchable. Returns false when the entry does not exist, was already promoted, or is no longer `Queued` (for example it was cancelled while its hook ran), so a cancelled entry stays cancelled |
| `CancelStaleAsync(olderThan, ct)` | `Task<int>` | Cancels every queued entry left unconfirmed for longer than `olderThan` and returns how many. The safe default for a stranded entry: nothing recorded tells a hook that succeeded from one that never ran or one that rejected the mutation, and cancelling keeps the entry visible. **It also deletes**, despite the name, the unconfirmed entries an earlier call cancelled once they were created more than `olderThan` plus 30 days ago; the returned count does not include them. See [Retention of cancelled staged entries](#retention-of-cancelled-staged-entries) |
| `PromoteStaleAsync(olderThan, ct)` | `Task<int>` | Promotes every queued entry left unconfirmed for longer than `olderThan` and returns how many. Only for hosts whose hooks are idempotent and whose chains re-check what the hook checked, since a promoted entry may be one whose hook never ran or rejected the mutation |

All three work on every data provider, including InMemory. The stale sweep that calls the last two runs only in the ManifestManager used with a database provider; the in-memory manifest manager (used when no database provider is configured) has no sweep, which does not matter because an in-memory store does not survive the process that could strand an entry.

## Retention of cancelled staged entries

A staged entry the sweep cancelled never ran, so it has no metadata, and metadata cleanup never removes it. `CancelStaleAsync` deletes such entries itself, so the scheduler's existing sweep cleans them up without a separate call:

- An entry is deleted once it was **created more than `olderThan` plus 30 days ago**. Nothing records when an entry was cancelled, so the age counts from creation. Thirty days matches the default dead-letter retention; it is fixed, not configurable.
- The delete runs **before** the call cancels anything, so an entry is never cancelled and deleted by the same call. A sweep that has not run for longer than the retention deletes what it cancels on its next call.
- It covers any **unconfirmed, cancelled** entry with no metadata, which includes an entry an operator cancelled while its hook ran. A confirmed entry an operator cancelled is left alone.
- A host that opted into `PromoteStaleStagedEntries()` never calls `CancelStaleAsync`, so nothing deletes the entries an operator cancelled mid-hook there.

Reconcile a vanished enqueue within that window: the cancelled entry, with its train name and input, is the only record of it.

## Who calls it

- `ITrainExecutionService.QueueAsync` calls `PromoteAsync` once a deferring train's hook returns. If the entry was cancelled in the meantime, `QueueAsync` throws `InvalidOperationException` rather than reporting success; if it was already confirmed by the sweep (with promotion opted in), `QueueAsync` succeeds.
- The ManifestManager's `ResolveStaleStagedEntriesJunction` calls `CancelStaleAsync`, or `PromoteStaleAsync` when the host called `PromoteStaleStagedEntries()`, with `StaleStagedEntryTimeout`. See [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) and [ManifestManager](/docs/scheduler/admin-trains/manifest-manager#resolvestalestagedentriesjunction).

## Package

```
dotnet add package Trax.Effect.Data
```
