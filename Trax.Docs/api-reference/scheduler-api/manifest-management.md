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
}
```

## Remarks

- `DisableAsync` sets `IsEnabled = false` on the manifest. The ManifestManager skips disabled manifests during polling.
- `TriggerAsync` creates a new execution independent of the regular schedule — the job's normal schedule continues unaffected. The work queue entry inherits the manifest's stored priority (no `DependentPriorityBoost` is applied for manual triggers).
- All three methods require the manifest to already exist. Use [ScheduleAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}) to create manifests first.
