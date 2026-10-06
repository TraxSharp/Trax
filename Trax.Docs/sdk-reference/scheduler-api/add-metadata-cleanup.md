---
layout: default
title: AddMetadataCleanup
description: Reference for AddMetadataCleanup, which purges old Metadata rows for the internal scheduler trains and any trains you add, with per-train retention.
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 8
---

# AddMetadataCleanup

Enables automatic purging of old metadata records for high-frequency system trains. The internal scheduler trains (`JobDispatcher`, `ManifestManager`, `MetadataCleanup`, `DeadLetterCleanup`, `JobRunner`) are always cleaned up while this is enabled. Additional consumer train types can be added via the configure callback.

## Signature

```csharp
public SchedulerConfigurationBuilder AddMetadataCleanup(
    Action<MetadataCleanupConfiguration>? configure = null
)
```

## Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `configure` | `Action<MetadataCleanupConfiguration>?` | No | `null` | Optional callback to customize cleanup behavior |

## Returns

`SchedulerConfigurationBuilder`, for continued fluent chaining.

## MetadataCleanupConfiguration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CleanupInterval` | `TimeSpan` | 1 minute | How often the cleanup background service runs. 1 second to 30 days |
| `RetentionPeriod` | `TimeSpan` | 30 minutes | How old metadata must be (in a terminal state) before eligible for deletion, for every train not given a retention of its own. This is the value the persisted runtime override replaces |
| `DeleteBatchSize` | `int?` | 1000 | Max runs selected per batch, per retention group, 1 to 10,000. `null` sweeps in batches of 10,000. A batch is one transaction, and can exceed the limit by the runs it must be deleted with: a run that replays another goes in the same batch as the run it replays. The transaction holds row locks on the batch's child runs and dead letters until it commits, so a larger batch delays a running child's status writes longer |

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `AddTrainType<TTrain>()` | `void AddTrainType<TTrain>() where TTrain : class` | Adds a train type to the cleanup whitelist by generic type, retained for `RetentionPeriod` |
| `AddTrainType<TTrain>(TimeSpan)` | `void AddTrainType<TTrain>(TimeSpan retention) where TTrain : class` | The same, retained for `retention` instead of `RetentionPeriod` |
| `AddTrainType(string)` | `void AddTrainType(string trainTypeName)` | Adds a train type to the cleanup whitelist by fully-qualified type name, retained for `RetentionPeriod` |
| `AddTrainType(string, TimeSpan)` | `void AddTrainType(string trainTypeName, TimeSpan retention)` | The same, retained for `retention` instead of `RetentionPeriod` |

The two-argument overloads throw:

- `ArgumentOutOfRangeException` when `retention` is zero or negative.
- `InvalidOperationException` when the train is an internal scheduler train, which is always swept at `RetentionPeriod`.
- `InvalidOperationException` when the same name was already added with a different retention. The same retention twice is accepted and is not counted twice.

`RetentionPeriod` and every per-train retention must be between one second and ten years, `CleanupInterval` between one second and 30 days, and `DeleteBatchSize` between 1 and 10,000 when set. `AddScheduler` checks them when the scheduler is built and fails with an `InvalidOperationException` naming each value out of range (see [Value Ranges](/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges)). To keep a train's metadata as long as possible, give it ten years.

A train declared once under its interface name and again under its class name with different retentions cannot be caught here, because those are unrelated strings until the train registry relates them. The host refuses to start instead.

## Examples

### Default Configuration

```csharp
.AddScheduler(scheduler => scheduler
    .AddMetadataCleanup()  // Cleans all internal scheduler train metadata
)
```

### Custom Configuration

```csharp
.AddScheduler(scheduler => scheduler
    .AddMetadataCleanup(cleanup =>
    {
        cleanup.RetentionPeriod = TimeSpan.FromHours(2);
        cleanup.CleanupInterval = TimeSpan.FromMinutes(5);
        cleanup.AddTrainType<MyHighFrequencyTrain>();
        cleanup.AddTrainType("MyNamespace.AnotherTrain");
    })
)
```

### A Retention Per Train

```csharp
.AddScheduler(scheduler => scheduler
    .AddMetadataCleanup(cleanup =>
    {
        cleanup.RetentionPeriod = TimeSpan.FromMinutes(30);
        cleanup.AddTrainType<IDeltaImportAllTrain>();                      // on the default
        cleanup.AddTrainType<IPatchCustomerTrain>(TimeSpan.FromDays(30));  // its own
    })
)
```

Trains sharing a cutoff are swept together, so the batched delete runs once per distinct retention. Use this when one train's metadata is worth keeping for a different reason than the rest, for example because its input is replayable and the others are noise. Pair it with [`ShouldSaveInputs`](/docs/sdk-reference/configuration/save-train-parameters) to decide whose input is stored in the first place.

## Remarks

- Only metadata in a **terminal state** (`Completed`, `Failed`, or `Cancelled`) older than `RetentionPeriod` is deleted. `Pending` and `InProgress` metadata is never cleaned up.
- A run is kept, whatever its age, while a `Queued` work queue entry or any metadata row names it in `replay_decisions_of`, so a [re-queue that replays its decisions](/docs/effect/decisions#a-requeue-of-a-requeue) can still read them. When the linking run has expired too, it is deleted first and the run it links to goes in a later batch or sweep. A batch is deleted all or nothing: the runs, and the work queue entries, logs, dead letter links and children's parent links they own, are cleared in one transaction whose delete repeats the keep test. When a run was linked after the cleanup selected it, the transaction rolls back, so the run keeps everything it owns, and the batch is rechecked and deleted without it. A batch linked for replay on three successive attempts is logged and skipped for the rest of the sweep.
- The cleanup service runs as an `IHostedService` on the configured `CleanupInterval`.
- The internal scheduler trains (`JobDispatcher`, `ManifestManager`, `MetadataCleanup`, `DeadLetterCleanup`, `JobRunner`) are always pruned while cleanup is enabled. You don't need to add them manually, and a consumer can never accidentally leave one out.
- A cleanup batch that hits an unexpected foreign-key reference or other error is bisected to isolate the offending row, which is logged and skipped, so one bad row can't abort the whole sweep.
- Train type names are matched against the `name` column in the metadata table (which stores the interface FullName, the canonical train name).
- **The persisted runtime override replaces `RetentionPeriod` only.** Editing the retention from the dashboard's Server Settings page, or through the `updateScheduler` mutation, moves the cutoff for every train on the default and leaves a retention passed to `AddTrainType` untouched. A per-train value is normally set because of what the row holds rather than as an operational preference, so shortening one is a code change. See [scheduler ADR 0003](https://github.com/TraxSharp/Trax/blob/main/Trax.Scheduler/docs/adr/0003-a-runtime-retention-override-replaces-only-the-default.md).
