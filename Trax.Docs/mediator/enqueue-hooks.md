---
title: Enqueue hooks
description: "OnQueue, DeferQueuePromotion and QueueSubjectKey on ServiceTrain: when the mediator's queue path calls each one, what it may do, and what it refuses."
type: reference
area: mediator
nav_order: 10
source:
  - Trax.Effect/src/Trax.Effect/Services/ServiceTrain/ServiceTrain.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/TrainExecutionService.cs
  - Trax.Effect/src/Trax.Effect/Data/Services/EnqueueContext/IEnqueueContextAccessor.cs
---

# Enqueue hooks

Three `protected virtual` members of `ServiceTrain` that the mediator's queue path
(`ITrainExecutionService.QueueAsync`) calls when it accepts work, before the train runs. Why they
exist and what they guarantee is in [How queued work stays durable and serialized](/docs/mediator/queued-work).

| Member | Default | Called |
|---|---|---|
| `Task OnQueue(Metadata metadata, CancellationToken ct)` | no-op | Before the work queue row is committed, or, for a train that defers promotion, after the row is staged unconfirmed and before it is confirmed |
| `bool DeferQueuePromotion` | `false` | Read once per enqueue |
| `string? QueueSubjectKey(Metadata metadata)` | `null` | Before the work queue row is written |

## Which paths call them

| Path | `QueueSubjectKey`, `OnQueue` |
|---|---|
| `ITrainExecutionService.QueueAsync` (GraphQL queue mutations, the dashboard's queue dialog and re-queue button) | Called |
| `IOperationsService.RunTrainAsync` (the dashboard's **Run** dialog) | `OnQueue` runs on the run's input before the run's row is written; a throw refuses the run. The key is not used: the run has no work queue entry. |
| `ITrainExecutionService.RunAsync`, `ITrainBus` | Not called |
| The scheduler running the queued entry later | Not called |
| Work queued from a manifest, a dormant dependent activated by its parent (`IDormantDependentContext.ActivateAsync`) | Not called |

The enqueue resolves the train once, in a DI scope it creates and disposes before it returns, and
calls `QueueSubjectKey`, `DeferQueuePromotion` and `OnQueue` on that one instance. `[Inject]`
properties are filled before the hooks run. Scoped services belong to the enqueue's scope, not
the caller's. A train that overrides neither `OnQueue` nor `QueueSubjectKey` is not resolved.

Inside the hooks, `this.Metadata` is null. The input is on the `metadata` argument
(`metadata.GetInput<T>()`), and `TrainInput` returns it too. `metadata.ExternalId` is the
ExternalId the eventual run executes under; `Id`, `ManifestId` and `ScheduledTime` are unset.

## OnQueue

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

public class ProcessMatchResultTrain
    : ServiceTrain<ProcessMatchResultInput, ProcessMatchResultOutput>,
        IProcessMatchResultTrain
{
    [Inject]
    public IDbContextFactory<GameDbContext>? GameDbFactory { get; set; }

    protected override Task<Either<Exception, ProcessMatchResultOutput>> Junctions() =>
        Chain<RecordMatchResultJunction>().Resolve();

    protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
    {
        var input = metadata.GetInput<ProcessMatchResultInput>();
        if (input is null || GameDbFactory is null)
            return;

        await using var db = await GameDbFactory.CreateDbContextAsync(ct);
        db.Matches.Add(new MatchRecord { MatchId = input.MatchId, Region = input.Region });
        await db.SaveChangesAsync(ct);
    }
}
```

| Behaviour | Detail |
|---|---|
| Exceptions | Propagate and abort the enqueue; no work queue row is left. The operations surface (the dashboard, the GraphQL `queueTrain` and `requeueExecution` mutations) shows `The enqueue was refused: <message>` for a plain `TrainException` and for the mediator's own cancelled and timed-out refusals, and `The enqueue was refused.` for anything else, logging the exception at `Warning`. |
| Re-execution | The run executes the full `Junctions()` chain later. An effect both the hook and the chain perform happens twice. |
| Time limit | A hook running longer than `MaxQueueHookDuration` (30 seconds by default, set with `AddMediator(m => m.WithMaxQueueHookDuration(TimeSpan))`) fails its enqueue with `QueueHookTimeoutException` and rolls back what was written on the enqueue's context. The hook's token is cancelled at the limit. A hook still running after its enqueue failed has its later enqueues refused, and what it throws is logged. The limit does not apply to a deferring train's hook. |
| Caller cancellation | The enqueue stops waiting and rolls back, as at the time limit. |

### IEnqueueContextAccessor.Current

`IEnqueueContextAccessor.Current` is the data context the enqueue is about to commit on. It is
non-null only while `OnQueue` runs for a train that does not defer promotion. Entities tracked on it
commit and roll back with the work queue row. The hook must not call `SaveChanges` or commit on
it. It covers entities in Trax's own model only. `Current` flows with the async call, so every
accessor instance on that flow sees it, whatever its lifetime or scope. See
[IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor).

```csharp fragment
protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
{
    await accessor.Current!.Track(someTraxEntity); // no SaveChanges: the enqueue commits it
}
```

The enqueue holds that context, a pooled connection and an open transaction while the hook runs.

### Enqueues started inside OnQueue

An enqueue started from inside an `OnQueue` hook joins the enqueue it runs inside: its entry is
written in the outer context and transaction, and it returns its entry's id as usual.

| Rule | Detail |
|---|---|
| A nested failure fails the outer enqueue | Even when the hook catches it. The outer enqueue throws `InvalidOperationException` with the nested failure as its inner exception. |
| Every nested enqueue must be awaited | One still running when the hook returns fails the outer enqueue. Several may run at once (`Task.WhenAll`); their writes are serialized. |
| A deferring train enqueued from a hook is not staged | Its entry is written confirmed. |
| A deferring train's hook has nothing to join | Its own entry is already committed, so an enqueue it starts commits on its own. The same holds on a provider without transactions. The in-memory provider joins but has no transaction to roll back. |

## DeferQueuePromotion

```csharp fragment
protected override bool DeferQueuePromotion => true;
```

| Step | Effect |
|---|---|
| 1 | The entry is committed unconfirmed and cannot be dispatched. The context it was committed on is released. |
| 2 | `OnQueue` runs. `IEnqueueContextAccessor.Current` is null. |
| 3 | A second commit stamps `confirmed_at` and the entry becomes claimable. This runs even when the caller has cancelled after the hook returned. |

| Case | Result |
|---|---|
| The entry was cancelled while the hook ran (by an operator or the stale-entry sweep) | `QueueAsync` throws `QueuedWorkCancelledException`, an `InvalidOperationException` carrying the entry's `WorkQueueId`. |
| The sweep promoted the entry while the hook ran | The enqueue succeeds and the entry runs. |
| The hook throws | The staged entry is removed if it is still staged; a promoted or dispatched entry is not deleted. A failure to remove it does not replace the hook's exception. |
| The process dies between the commits | The entry stays unconfirmed. The ManifestManager's sweep cancels entries unconfirmed for longer than `StaleStagedEntryTimeout` (10 minutes by default), or promotes them when the host calls `PromoteStaleStagedEntries()`. See [ManifestManager](/docs/scheduler/admin-trains/manifest-manager#resolvestalestagedentriesjunction). |

## QueueSubjectKey

```csharp fragment
protected override string? QueueSubjectKey(Metadata metadata) =>
    $"customer-{metadata.GetInput<PatchCustomerInput>()!.CustomerId}";
```

The dispatcher does not claim an entry while another entry with the same key has a run that has not
reached a terminal state. `null` means no serialization.

| Rule | Detail |
|---|---|
| Comparison | Exact, case-sensitive, across all trains. Keys are not namespaced by train. |
| Refused keys | Empty or whitespace only; containing an unpaired surrogate; containing a NUL character; longer than 512 Unicode characters (`WorkQueue.MaxSubjectKeyLength`; a character outside the Basic Multilingual Plane counts once). `WorkQueue.Create` applies the rules, and the enqueue reports a refusal as an `InvalidOperationException` naming the train. |
| Exceptions | Propagate and abort the enqueue. |
| Work that carries a key | Entries created through the mediator's queue path, including the dashboard's queue dialog and re-queue button. |
| Work that carries none | Work queued from a manifest (its runs, retries and dead-letter requeues), dormant dependents, synchronous runs (`RunAsync`, `ITrainBus`), the dashboard's **Run** dialog. |
| Run-now of a keyed train | `IOperationsService.RunTrainAsync` refuses a train that overrides `QueueSubjectKey` outside a trusted execution scope, such as the dashboard's, and says to queue it. The dashboard's **Run** dialog warns that it bypasses serialization. |
| Order within a subject | Dispatch order: priority, then age. An entry whose `scheduledAt` has not arrived is not a candidate. Order holds with one dispatcher; with `MaxConcurrentDispatch` above 1 or several dispatching hosts, only mutual exclusion holds. |
| Per cycle | Dispatch offers only the first queued entry per subject. The dashboard's work queue detail page shows what an entry waits on under **Waiting On**. |
| Claim | On Postgres the claim takes a transaction-scoped advisory lock on the subject, held only for the claim. SQLite needs none. See [Multi-Server Concurrency](/docs/scheduler/concurrency#subject-serialization-advisory-lock-per-subject). |

A subject is released when its run reaches any terminal state, including `Failed` written by a
stale-run reaper (`StalePendingTimeout`, 20 minutes by default; `StaleInProgressTimeout`, 60 minutes
by default) or by the scheduler's startup recovery (`RecoverStuckJobsOnStartup`, on by default).
[How queued work stays durable and serialized](/docs/mediator/queued-work#what-bounds-subject-serialization)
explains what that means for the guarantee.

## See also

- [How queued work stays durable and serialized](/docs/mediator/queued-work)
- [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train), [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service), [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor), [IWorkQueuePromotion](/docs/sdk-reference/scheduler-api/i-work-queue-promotion)
- [Trusted execution scope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope)

## SDK Reference

> [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [IEnqueueContextAccessor](/docs/sdk-reference/mediator-api/i-enqueue-context-accessor) | [IWorkQueuePromotion](/docs/sdk-reference/scheduler-api/i-work-queue-promotion) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
