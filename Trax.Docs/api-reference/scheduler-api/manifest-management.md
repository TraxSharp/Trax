---
layout: default
title: Manifest Management
parent: Scheduler API
grand_parent: API Reference
nav_order: 6
---

# Manifest Management

Runtime methods on `IManifestScheduler` for controlling scheduled jobs. These are injected via DI and called at runtime — they are not available during startup configuration.

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

Triggers immediate execution of a scheduled job, independent of its normal schedule.

```csharp
Task TriggerAsync(string externalId, CancellationToken ct = default)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | The `ExternalId` of the manifest to trigger |
| `ct` | `CancellationToken` | No | Cancellation token |

**Throws**: `InvalidOperationException` when no manifest with the specified `ExternalId` exists.

## CancelAsync

Cancels all currently running executions of a scheduled job. Sets `CancellationRequested = true` on all InProgress metadata for the manifest and attempts same-server instant cancellation via the `ICancellationRegistry`. Cancelled trains transition to `TrainState.Cancelled` and are **not retried**.

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

Cancels all currently running executions for all manifests in a manifest group.

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
public class SchedulerController(IManifestScheduler scheduler) : ControllerBase
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
}
```

## Remarks

- `DisableAsync` sets `IsEnabled = false` on the manifest. The ManifestManager skips disabled manifests during polling.
- `TriggerAsync` creates a new execution independent of the regular schedule — the job's normal schedule continues unaffected. The work queue entry inherits the manifest's stored priority (no `DependentPriorityBoost` is applied for manual triggers).
- `CancelAsync` uses dual-layer cancellation: a database flag (`CancellationRequested = true`) for cross-server support, plus `ICancellationRegistry.TryCancel()` for same-server instant cancellation. Cancelled trains are **not retried** and **do not create dead letters**.
- `CancelGroupAsync` applies the same dual-layer cancellation to all in-progress executions across all manifests in the group.
- All methods (except `CancelGroupAsync`) require the manifest to already exist. Use [ScheduleAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}) to create manifests first.
