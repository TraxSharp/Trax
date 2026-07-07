---
layout: default
title: AddMetadataCleanup
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
| `CleanupInterval` | `TimeSpan` | 1 minute | How often the cleanup background service runs |
| `RetentionPeriod` | `TimeSpan` | 30 minutes | How old metadata must be (in a terminal state) before eligible for deletion |
| `DeleteBatchSize` | `int?` | 1000 | Max rows deleted per batch. Set to `null` for single-statement deletes |

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `AddTrainType<TTrain>()` | `void AddTrainType<TTrain>() where TTrain : class` | Adds a train type to the cleanup whitelist by generic type |
| `AddTrainType(string)` | `void AddTrainType(string trainTypeName)` | Adds a train type to the cleanup whitelist by fully-qualified type name |

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

## Remarks

- Only metadata in a **terminal state** (`Completed`, `Failed`, or `Cancelled`) older than `RetentionPeriod` is deleted. `Pending` and `InProgress` metadata is never cleaned up.
- The cleanup service runs as an `IHostedService` on the configured `CleanupInterval`.
- The internal scheduler trains (`JobDispatcher`, `ManifestManager`, `MetadataCleanup`, `DeadLetterCleanup`, `JobRunner`) are always pruned while cleanup is enabled. You don't need to add them manually, and a consumer can never accidentally leave one out.
- A cleanup batch that hits an unexpected foreign-key reference or other error is bisected to isolate the offending row, which is logged and skipped, so one bad row can't abort the whole sweep.
- Train type names are matched against the `name` column in the metadata table (which stores the interface FullName, the canonical train name).
