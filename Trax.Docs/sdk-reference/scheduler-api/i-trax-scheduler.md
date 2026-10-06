---
layout: default
title: ITraxScheduler
description: Reference for ITraxScheduler, the runtime API to create, change, trigger and cancel manifests, trigger groups and resolve dead letters, with its result types.
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 0.5
---

# ITraxScheduler

The runtime scheduling API. Inject it to create, change, trigger and cancel manifests while the application runs, and to resolve dead letters. `AddScheduler` registers it. The startup equivalent, which seeds manifests when the host starts, is the [scheduler builder](/docs/sdk-reference/scheduler-api/add-scheduler).

## Signature

```csharp
namespace Trax.Scheduler.Services.TraxScheduler;

public interface ITraxScheduler
{
    // Scheduling
    Task<Manifest> ScheduleAsync<TTrain, TInput, TOutput>(string externalId, TInput input, Schedule schedule,
        Action<ScheduleOptions>? options = null, CancellationToken ct = default);
    Task<IReadOnlyList<Manifest>> ScheduleManyAsync<TTrain, TInput, TOutput, TSource>(IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map, Schedule schedule,
        Action<ScheduleOptions>? options = null, Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default);
    Task<Manifest> ScheduleDependentAsync<TTrain, TInput, TOutput>(string externalId, TInput input,
        string dependsOnExternalId, Action<ScheduleOptions>? options = null, CancellationToken ct = default);
    Task<IReadOnlyList<Manifest>> ScheduleManyDependentAsync<TTrain, TInput, TOutput, TSource>(
        IEnumerable<TSource> sources, Func<TSource, (string ExternalId, TInput Input)> map,
        Func<TSource, string> dependsOn, Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null, CancellationToken ct = default);
    Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(TInput input, TimeSpan delay,
        Action<ScheduleOptions>? options = null, CancellationToken ct = default);
    Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(string externalId, TInput input, TimeSpan delay,
        Action<ScheduleOptions>? options = null, CancellationToken ct = default);

    // Manifest control
    Task DisableAsync(string externalId, CancellationToken ct = default);
    Task EnableAsync(string externalId, CancellationToken ct = default);
    Task TriggerAsync(string externalId, CancellationToken ct = default);
    Task TriggerAsync(string externalId, TimeSpan delay, CancellationToken ct = default);
    Task<ManifestTriggerResult> TriggerAsync(string externalId, bool askAfresh, CancellationToken ct = default);
    Task<ManifestTriggerResult> TriggerAsync(string externalId, TimeSpan delay, bool askAfresh, CancellationToken ct = default);
    Task<int> TriggerGroupAsync(long groupId, CancellationToken ct = default);
    Task<int> CancelAsync(string externalId, CancellationToken ct = default);
    Task<int> CancelGroupAsync(long groupId, CancellationToken ct = default);

    // Dead letters
    Task<DeadLetterOperationResult> RequeueDeadLetterAsync(long deadLetterId, CancellationToken ct = default);
    Task<DeadLetterOperationResult> AcknowledgeDeadLetterAsync(long deadLetterId, string note, CancellationToken ct = default);
    Task<BatchDeadLetterResult> RequeueDeadLettersAsync(long[] deadLetterIds, CancellationToken ct = default);
    Task<BatchDeadLetterResult> AcknowledgeDeadLettersAsync(long[] deadLetterIds, string note, CancellationToken ct = default);
    Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(CancellationToken ct = default);
    Task<BatchDeadLetterResult> AcknowledgeAllDeadLettersAsync(string note, CancellationToken ct = default);

    // Dead letters, asking the manifest's deciders afresh on request
    Task<DeadLetterOperationResult> RequeueDeadLetterAsync(long deadLetterId, bool askAfresh, CancellationToken ct = default);
    Task<BatchDeadLetterResult> RequeueDeadLettersAsync(long[] deadLetterIds, bool askAfresh, CancellationToken ct = default);
    Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(bool askAfresh, CancellationToken ct = default);
    Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(bool askAfresh, IProgress<int>? progress, CancellationToken ct = default);
}
```

Every scheduling method constrains `TTrain : IServiceTrain<TInput, TOutput>` and `TInput : IManifestProperties`.

## Members

| Method | Page |
|--------|------|
| `ScheduleAsync` | [Schedule](/docs/sdk-reference/scheduler-api/schedule) |
| `ScheduleManyAsync` | [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many) |
| `ScheduleDependentAsync`, `ScheduleManyDependentAsync` | [Dependent Scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling) |
| `ScheduleOnceAsync`, `DisableAsync`, `EnableAsync`, `TriggerAsync`, `CancelAsync`, `CancelGroupAsync` | [Manifest Management](/docs/sdk-reference/scheduler-api/manifest-management) |
| `TriggerGroupAsync` | [Below](#triggergroupasync) |
| The dead-letter methods | [Below](#dead-letters) |

## TriggerGroupAsync

Queues an immediate run of every eligible manifest in a manifest group.

```csharp
Task<int> TriggerGroupAsync(long groupId, CancellationToken ct = default)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `groupId` | `long` | The manifest group's id |
| `ct` | `CancellationToken` | Cancellation token |

**Returns**: the number of manifests queued.

- Only **enabled** manifests with a non-dependent schedule type (`None`, `Cron`, `Interval`, `OnDemand`) are queued. Dependent and dormant dependent manifests are skipped: they rely on a parent's completion and may have no standalone input.
- Each entry is marked as asked for by name, the way `TriggerAsync` marks one, so disabling a manifest after the group trigger does not hold its run.
- A manifest that already has a queued entry is not counted and does not stop the others. That entry is marked the same way, and brought forward to now if it was due later. The number skipped is logged.

## Dead letters

A manifest whose failures exceed its retry limit is dead-lettered and stops being scheduled until someone resolves it. Each method below resolves dead letters in `AwaitingIntervention`; anything already resolved is left alone. Resolving either way resets the manifest's failure counter, so it resumes normal scheduling.

| Method | What it does |
|--------|--------------|
| `RequeueDeadLetterAsync(deadLetterId)` | Queues a new run of the manifest and marks the dead letter `Retried`. A manifest holds one queued entry at a time, so when it already has one the result is a failure and the dead letter stays awaiting intervention. |
| `AcknowledgeDeadLetterAsync(deadLetterId, note)` | Marks it `Acknowledged` with your note, without retrying |
| `RequeueDeadLettersAsync(ids)` | As the single requeue, for up to 1000 ids. At most one entry is queued per manifest: dead letters that share a manifest are folded into one entry and all resolved; one whose manifest already has a queued entry is skipped. |
| `AcknowledgeDeadLettersAsync(ids, note)` | As the single acknowledge, for up to 1000 ids |
| `RequeueAllDeadLettersAsync()` | Requeues every dead letter awaiting intervention, a page of manifests at a time, so a large backlog is never loaded at once |
| `RequeueAllDeadLettersAsync(askAfresh, progress)` | As requeue-all, telling `progress` the running total of dead letters resolved after each page commits, on the fold's own thread. The requeue-all background job reads it to say how far it has got. An implementation that predates this overload runs the fold without reporting |
| `AcknowledgeAllDeadLettersAsync(note)` | Acknowledges every dead letter awaiting intervention. On a relational store it acknowledges 50,000 rows per statement in id order, so no statement outlasts the command timeout however large the backlog; a failure part-way leaves the pages done acknowledged, and acknowledging all again finishes the rest |

A batch call with an empty list, or more than 1000 ids, is refused: the result counts nothing and its message says why.

On a host `AddScheduler()` built without a database provider (the in-memory store), the three requeues are refused with `OperationsService.NoDispatcherMessage` and change nothing: no job dispatcher runs there, so a requeued entry would never start, though the in-memory manifest manager still dead-letters. The single requeue returns a failed result; the batch and requeue-all count nothing, and the requeue-all background job finishes with that message and a count of 0. Acknowledging is unaffected. A `TraxScheduler` constructed by hand is not refused ([scheduler ADR 0019](https://github.com/TraxSharp/Trax/blob/main/Trax.Scheduler/docs/adr/0019-a-queued-run-is-refused-where-nothing-dispatches-it.md)).

A note longer than `TraxScheduler.MaxAcknowledgeNoteLength` (1,000 characters) is refused by all three acknowledge methods, and nothing changes: the single acknowledge returns a failed result and the batch and all variants count nothing, each with the message `The note is {n} characters; it may be at most 1000 characters.` A note is an operator's reason, and acknowledge-all writes it onto every awaiting row. The dashboard and the API both acknowledge through these methods.

### Replaying the failed run's decisions

A requeued run replays the decisions the manifest's failed run recorded, when that is sound, as an
automatic retry does: it takes the tracks the failed run's deciders chose instead of asking them
again. See [Retries replay decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions)
for the checks. Each requeue has an overload that takes `askAfresh`:

| `askAfresh` | The requeued run |
|---|---|
| `false` | Replays the failed run's decisions when the checks pass, as the overload without it does |
| `true` | Asks its deciders afresh |

The `askAfresh` overloads, and those on `TriggerAsync`, have default implementations on the
interface that throw `NotSupportedException`, so an implementation written before them still
compiles and refuses the option rather than ignoring it.

### Result types

```csharp
public record DeadLetterOperationResult(bool Success, long? WorkQueueId, string Message);
public record BatchDeadLetterResult(int Count, string Message);
```

| Field | Description |
|-------|-------------|
| `DeadLetterOperationResult.Success` | Whether the dead letter was resolved |
| `DeadLetterOperationResult.WorkQueueId` | The queued entry a requeue created; `null` for an acknowledge or a failure |
| `DeadLetterOperationResult.Message` | What happened, including why it failed (not found, already resolved, already queued) |
| `BatchDeadLetterResult.Count` | How many dead letters were resolved |
| `BatchDeadLetterResult.Message` | A summary that also counts the folded and skipped dead letters |

These are the same operations the dashboard and the GraphQL `operations` namespace use. See [Dead Letters](/docs/scheduler/dead-letters-and-cleanup#handling-dead-letters) for the concepts.

## Example

```csharp
public class GroupController(ITraxScheduler scheduler)
{
    public async Task<string> RunGroupNow(long groupId, CancellationToken ct)
    {
        var queued = await scheduler.TriggerGroupAsync(groupId, ct);
        return $"{queued} manifest(s) queued.";
    }

    public Task<BatchDeadLetterResult> RetryEverything(CancellationToken ct) =>
        scheduler.RequeueAllDeadLettersAsync(ct);
}
```

## Package

```
dotnet add package Trax.Scheduler
```
