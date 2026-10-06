---
layout: default
title: Manifest Management
description: "Reference for the ITraxScheduler methods that control scheduled jobs at runtime: DisableAsync, EnableAsync, TriggerAsync, ScheduleOnceAsync and CancelAsync."
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 6
---

# Manifest Management

Runtime methods on `ITraxScheduler` for controlling scheduled jobs. These are injected via DI and called at runtime. They are not available during startup configuration.

## DisableAsync

Disables a scheduled job, preventing future executions. The manifest is **not deleted**, only disabled.

```csharp
Task DisableAsync(string externalId, CancellationToken ct = default)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | The `ExternalId` of the manifest to disable |
| `ct` | `CancellationToken` | No | Cancellation token |

**Throws**: `InvalidOperationException` when no manifest with the specified `ExternalId` exists.

## EnableAsync

Re-enables a previously disabled scheduled job.

```csharp
Task EnableAsync(string externalId, CancellationToken ct = default)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | The `ExternalId` of the manifest to enable |
| `ct` | `CancellationToken` | No | Cancellation token |

**Throws**: `InvalidOperationException` when no manifest with the specified `ExternalId` exists.

## TriggerAsync

Triggers execution of a scheduled job, independent of its normal schedule. The overload with `delay` creates a work queue entry with a future `ScheduledAt`. The JobDispatcher skips it until that time arrives.

A manifest holds at most one queued work queue entry. When it already has one, both overloads queue nothing more and return normally, and the entry already there becomes the triggered run: it is marked as asked for by name, so it runs even if the manifest is disabled, and an entry due later than the trigger asks (a retry waiting out its backoff, or an earlier delayed trigger) is brought forward to now, or to now plus `delay` for the delayed overload. Brought forward to now, it stores no `ScheduledAt`, as a new immediate entry does, so it is due on the dispatcher's next poll whichever of the host's and the database's clocks is ahead. An entry due sooner keeps its time. The log says whether the trigger queued an entry, moved one forward, or found one already due. `ITraxScheduler.TriggerGroupAsync` does the same for each enabled member: a member with an entry already queued is not counted in the number it returns, but that entry is marked and brought forward to now.

```csharp
Task TriggerAsync(string externalId, CancellationToken ct = default)
```

```csharp
Task TriggerAsync(string externalId, TimeSpan delay, CancellationToken ct = default)
```

```csharp
Task<ManifestTriggerResult> TriggerAsync(string externalId, bool askAfresh, CancellationToken ct = default)
Task<ManifestTriggerResult> TriggerAsync(string externalId, TimeSpan delay, bool askAfresh, CancellationToken ct = default)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | The `ExternalId` of the manifest to trigger |
| `delay` | `TimeSpan` | No | How far in the future to schedule the execution. When omitted, the job is queued for immediate dispatch. |
| `askAfresh` | `bool` | No | When `true` and the trigger releases a queued entry that would [replay a failed run's decisions](/docs/scheduler/dead-letters-and-cleanup#retries-replay-decisions) (a retry waiting out its backoff), the entry no longer replays them: the run asks its deciders afresh. A new entry never replays, so it changes nothing there. |
| `ct` | `CancellationToken` | No | Cancellation token |

**Throws**: `InvalidOperationException` when no manifest with the specified `ExternalId` exists. The `askAfresh` overloads throw `NotSupportedException` from an `ITraxScheduler` implementation written before them.

### ManifestTriggerResult

The `askAfresh` overloads return what the trigger did:

```csharp
public record ManifestTriggerResult(
    long WorkQueueId,
    bool Created,
    DateTime? ScheduledAt,
    bool AlreadyDispatched,
    long? ReplayDecisionsOf)
{
    public bool MovedForward { get; init; }
}
```

| Field | Description |
|---|---|
| `WorkQueueId` | The work queue entry that runs the triggered run |
| `Created` | `true` when the trigger queued a new entry; `false` when it released the manifest's queued entry instead |
| `ScheduledAt` | When the entry is due; null means immediately |
| `AlreadyDispatched` | `true` when the dispatcher claimed the manifest's queued entry between the trigger finding it and changing it, so the trigger changed nothing about it: it was not brought forward, and a run asked afresh still replays the decisions it was queued to replay |
| `ReplayDecisionsOf` | The run whose decisions the triggered run replays, as the trigger left the entry; null when it asks its deciders afresh. A new entry never replays. |
| `MovedForward` | `true` when the manifest's queued entry was due later than the trigger asked, so the trigger brought it forward to `ScheduledAt`; `false` for a new entry, for an entry already due by then, and for one the dispatcher claimed |

`IOperationsService.TriggerManifestAsync` returns this record inside a `TriggerManifestResult` whose message says the same thing in words; the dashboard and the API show that message.

The trigger clears a queued retry's link only while the entry is still queued. With
`AlreadyDispatched` and a `ReplayDecisionsOf`, the run was asked afresh too late and replays that
run's decisions; the trigger also logs a warning. The
[`triggerManifest`](/docs/sdk-reference/graphql-api/mutations#triggermanifest) mutation reports the
same case in its message.

## ScheduleOnceAsync

Creates a one-off manifest with `ScheduleType.Once` that fires after the specified delay and auto-disables on success. Unlike `TriggerAsync`, this does not require a pre-existing manifest.

```csharp
Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(
    TInput input,
    TimeSpan delay,
    Action<ScheduleOptions>? options = null,
    CancellationToken ct = default
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties
```

```csharp
Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(
    string externalId,
    TInput input,
    TimeSpan delay,
    Action<ScheduleOptions>? options = null,
    CancellationToken ct = default
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | No | A unique identifier for this one-off job. When omitted, auto-generated as `once-{guid}`. |
| `input` | `TInput` | Yes | The input data passed to the train on execution. |
| `delay` | `TimeSpan` | Yes | How far in the future to schedule the execution. `ScheduledAt` is set to `DateTime.UtcNow + delay`. |
| `options` | `Action<ScheduleOptions>?` | No | Optional callback to configure manifest options (MaxRetries, Timeout, Priority, Group). |
| `ct` | `CancellationToken` | No | Cancellation token |

**Returns**: `Task<Manifest>`, the created manifest record.

**Auto-disable**: When the job completes successfully, `IsEnabled` is set to `false` on the manifest. The manifest remains in the database for audit purposes but is skipped by the ManifestManager on subsequent cycles. If the job fails, normal retry logic applies until it succeeds (and auto-disables) or its failures exceed `MaxRetries` (and it is dead-lettered). If its run is cancelled, it is not run again.

## CancelAsync

Cancels all pending and running executions of a scheduled job. Sets `CancellationRequested = true` on all Pending and InProgress metadata for the manifest and attempts same-server instant cancellation via the `ICancellationRegistry`. Cancelled trains transition to `TrainState.Cancelled` and are **not retried**: the cancelled run consumes the occurrence it ran for, so the manifest next runs at its next scheduled occurrence (a `Once` manifest not at all, a dependent at its parent's next success).

```csharp
Task<int> CancelAsync(string externalId, CancellationToken ct = default)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | The `ExternalId` of the manifest whose executions should be cancelled |
| `ct` | `CancellationToken` | No | Cancellation token |

**Returns**: The number of metadata records that had cancellation requested. Returns `0` if no in-progress executions exist.

**Throws**: `InvalidOperationException` when no manifest with the specified `ExternalId` exists.

## CancelGroupAsync

Cancels all pending and running executions for all manifests in a manifest group, by the same rule as `CancelAsync`.

```csharp
Task<int> CancelGroupAsync(long groupId, CancellationToken ct = default)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `long` | Yes | The ID of the manifest group whose executions should be cancelled |
| `ct` | `CancellationToken` | No | Cancellation token |

**Returns**: The number of metadata records that had cancellation requested. Returns `0` if no in-progress executions exist in the group.

## Example

```csharp
public class SchedulerController(ITraxScheduler scheduler) : ControllerBase
{
    [HttpPost("jobs/{externalId}/disable")]
    public async Task<IActionResult> Disable(string externalId)
    {
        await scheduler.DisableAsync(externalId);
        return Ok();
    }

    [HttpPost("jobs/{externalId}/enable")]
    public async Task<IActionResult> Enable(string externalId)
    {
        await scheduler.EnableAsync(externalId);
        return Ok();
    }

    [HttpPost("jobs/{externalId}/trigger")]
    public async Task<IActionResult> Trigger(string externalId)
    {
        await scheduler.TriggerAsync(externalId);
        return Ok();
    }

    [HttpPost("jobs/{externalId}/trigger-delayed")]
    public async Task<IActionResult> TriggerDelayed(string externalId, [FromQuery] int delayMinutes)
    {
        await scheduler.TriggerAsync(externalId, TimeSpan.FromMinutes(delayMinutes));
        return Ok();
    }

    [HttpPost("jobs/{externalId}/cancel")]
    public async Task<IActionResult> Cancel(string externalId)
    {
        var count = await scheduler.CancelAsync(externalId);
        return Ok(new { cancelled = count });
    }

    [HttpPost("groups/{groupId}/cancel")]
    public async Task<IActionResult> CancelGroup(long groupId)
    {
        var count = await scheduler.CancelGroupAsync(groupId);
        return Ok(new { cancelled = count });
    }

    [HttpPost("jobs/schedule-once")]
    public async Task<IActionResult> ScheduleOnce([FromBody] ScheduleOnceRequest request)
    {
        var manifest = await scheduler.ScheduleOnceAsync<ISendReminderTrain, SendReminderInput, Unit>(
            request.ExternalId,
            new SendReminderInput { UserId = request.UserId },
            TimeSpan.FromMinutes(request.DelayMinutes));
        return Ok(new { manifestId = manifest.Id, externalId = manifest.ExternalId });
    }
}
```

## Remarks

- `DisableAsync` sets `IsEnabled = false` on the manifest. The ManifestManager skips disabled manifests during polling, and the dispatcher holds their scheduled entries until they are re-enabled. `TriggerAsync`, `TriggerGroupAsync` and a dead-letter requeue still run a disabled manifest (see [Disabling a job](/docs/scheduler/scheduling-options#disabling-a-job)).
- `TriggerAsync` creates a new execution independent of the regular schedule. The job's normal schedule continues, measured like any run's from when the triggered run succeeds or is cancelled. The work queue entry inherits the manifest's stored priority (no `DependentPriorityBoost` is applied for manual triggers). The `delay` overload sets `ScheduledAt` on the work queue entry; the JobDispatcher skips entries with a future `ScheduledAt`.
- `ScheduleOnceAsync` creates a manifest with `ScheduleType.Once`. The manifest auto-disables (`IsEnabled = false`) after its first successful execution. If no `externalId` is provided, one is generated as `once-{guid}`. Uses upsert semantics, so it is safe to call with the same `externalId` without creating duplicates.
- `CancelAsync` uses dual-layer cancellation: a database flag (`CancellationRequested = true`) for cross-server support, plus `ICancellationRegistry.TryCancel()` for same-server instant cancellation. Cancelled trains are **not retried** and **do not create dead letters**; the schedule resumes at the occurrence after the cancelled run.
- `CancelGroupAsync` applies the same dual-layer cancellation to all pending and in-progress executions across all manifests in the group.
- When either method flags at least one run it raises the `Execution` change signal (`ChangeDomain.Execution`), so an `onDataChanged` subscriber refetches its runs view at once rather than when the cancellation takes effect.
- A Pending run is recorded `Cancelled` and never run when the job runner picks it up, on any host, with or without `AddJunctionProgress()`. Both methods follow the rule [IOperationsService.CancelExecutionsAsync](/docs/sdk-reference/scheduler-api/i-operations-service#batch-actions) applies to a list of runs; before this they took InProgress runs only.
- All methods (except `CancelGroupAsync` and `ScheduleOnceAsync`) require the manifest to already exist. Use [ScheduleAsync](/docs/sdk-reference/scheduler-api/schedule) to create manifests first.
