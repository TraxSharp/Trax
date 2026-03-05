---
layout: default
title: Delayed / One-Off Jobs
parent: Scheduling
nav_order: 3
---

# Delayed / One-Off Jobs

## The Problem

Some work doesn't fit a recurring schedule. You need to send a reminder email in 30 minutes, process a refund after a 24-hour cooling period, or run a one-time data migration 5 minutes from now. These are fire-once jobs with a delay — not periodic, not dependent on another manifest, just "run this once, later."

Trax supports two approaches: delayed triggers on existing manifests, and standalone one-off manifests that auto-disable after completion.

## Delayed Trigger: TriggerAsync with Delay

If a manifest already exists — registered at startup or created via `ScheduleAsync` — you can trigger it with a delay. This creates a work queue entry with a future `ScheduledAt` timestamp. The JobDispatcher skips the entry until that time arrives.

```csharp
// Trigger an existing manifest 30 minutes from now
await scheduler.TriggerAsync("send-reminder", TimeSpan.FromMinutes(30));
```

The manifest's regular schedule (if any) continues unaffected. The delayed trigger is an independent execution — it doesn't reset or interfere with the normal cadence.

This is useful when you already have a manifest for the train and want to queue an extra execution at a future time. The manifest must exist; `TriggerAsync` throws `InvalidOperationException` if it doesn't.

## One-Off Jobs: ScheduleOnceAsync

For work that has no pre-existing manifest — a transient job that should run once and never again — use `ScheduleOnceAsync`. It creates a manifest with `ScheduleType.Once`, sets `ScheduledAt` to the current time plus the delay, and auto-disables the manifest after the first successful execution.

### Runtime API (ITraxScheduler)

```csharp
// Auto-generated externalId (format: "once-{guid}")
await scheduler.ScheduleOnceAsync<ISendReminderTrain, SendReminderInput>(
    new SendReminderInput { UserId = userId, Message = "Your trial expires tomorrow" },
    TimeSpan.FromHours(24));

// Explicit externalId for tracking or idempotency
await scheduler.ScheduleOnceAsync<IProcessRefundTrain, ProcessRefundInput>(
    "refund-order-12345",
    new ProcessRefundInput { OrderId = 12345 },
    TimeSpan.FromHours(24));
```

When no `externalId` is provided, one is auto-generated in the format `once-{guid}`. If you need to cancel or query the job later, pass an explicit ID.

### Startup Configuration (Builder Pattern)

For one-off jobs that should be scheduled when the application starts — such as a post-deployment migration or a delayed initialization task — use the builder:

```csharp
services.AddTraxEffects(options => options
    .AddScheduler(scheduler => scheduler
        .UseLocalWorkers()
        .ScheduleOnce<IPostDeployTrain>(
            "post-deploy-v2.5",
            new PostDeployInput { Version = "2.5" },
            TimeSpan.FromMinutes(5))
    )
);
```

Like `Schedule`, the builder captures the manifest and seeds it on startup with upsert semantics. If a manifest with the same `externalId` already exists, it is updated rather than duplicated — so restarting the application doesn't create duplicate jobs.

## Auto-Disable Behavior

When a `ScheduleType.Once` manifest completes successfully, the `UpdateManifestSuccessStep` sets `IsEnabled = false` on the manifest. The manifest stays in the database for audit purposes — you can see its execution history in the dashboard — but the ManifestManager skips it on subsequent polling cycles.

If the job fails, normal retry logic applies. Retries continue until the job succeeds (and auto-disables) or exceeds `MaxRetries` (and is dead-lettered). A dead-lettered once-manifest can be retried from the dashboard like any other dead letter.

## How It Works Internally

1. **Manifest creation**: `ScheduleOnceAsync` creates a manifest with `ScheduleType = Once` and `ScheduledAt` set to `DateTime.UtcNow + delay`.
2. **Queuing**: The ManifestManager's `DetermineJobsToQueueStep` evaluates Once manifests via `ShouldRunOnce` — it queues the manifest when the current time is at or past `ScheduledAt`.
3. **Dispatch filtering**: The JobDispatcher's `LoadQueuedJobsStep` filters out work queue entries whose `ScheduledAt` is still in the future. This applies to both delayed triggers and Once manifests.
4. **Auto-disable on success**: The `UpdateManifestSuccessStep` checks if the manifest is `ScheduleType.Once`. If so, it sets `IsEnabled = false` after recording the successful execution.

## When to Use Which

| Scenario | API | Why |
|----------|-----|-----|
| Extra run of an existing scheduled job, delayed | `TriggerAsync(externalId, delay)` | The manifest already exists. You just want an additional execution at a future time. |
| One-time job, no existing manifest | `ScheduleOnceAsync(input, delay)` | No manifest exists yet. The job runs once and auto-disables. |
| One-time job you need to track or cancel | `ScheduleOnceAsync(externalId, input, delay)` | Same as above, but with a predictable ID for `CancelAsync` or dashboard lookup. |
| Post-deployment one-time task | `scheduler.ScheduleOnce<TTrain>(...)` at startup | Declarative, upsert-safe, runs once after a delay on app start. |
| Immediate execution of an existing job | `TriggerAsync(externalId)` (no delay) | Use the existing zero-delay overload. |

## Configuration

One-off jobs accept the same `ScheduleOptions` as recurring manifests:

```csharp
await scheduler.ScheduleOnceAsync<ICleanupTrain, CleanupInput>(
    "cleanup-temp-files",
    new CleanupInput { OlderThan = TimeSpan.FromDays(7) },
    TimeSpan.FromMinutes(10),
    options => options
        .MaxRetries(1)
        .Timeout(TimeSpan.FromMinutes(5))
        .Group("maintenance"));
```

*SDK Reference: [TriggerAsync]({{ site.baseurl }}{% link sdk-reference/scheduler-api/manifest-management.md %}#triggerasync), [ScheduleOnceAsync]({{ site.baseurl }}{% link sdk-reference/scheduler-api/manifest-management.md %}#scheduleonceasync), [ScheduleOptions]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule.md %}#scheduleoptions)*
