---
layout: default
title: AddMetadataCleanup
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 8
---

# AddMetadataCleanup

Enables automatic purging of old metadata records for high-frequency system trains. By default, cleans up `ManifestManagerTrain` and `MetadataCleanupTrain` metadata. Additional train types can be added via the configure callback.

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

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## MetadataCleanupConfiguration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CleanupInterval` | `TimeSpan` | 1 minute | How often the cleanup background service runs |
| `RetentionPeriod` | `TimeSpan` | 30 minutes | How old metadata must be (in a terminal state) before eligible for deletion |

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `AddTrainType<TTrain>()` | `void AddTrainType<TTrain>() where TTrain : class` | Adds a train type to the cleanup whitelist by generic type |
| `AddTrainType(string)` | `void AddTrainType(string trainTypeName)` | Adds a train type to the cleanup whitelist by fully-qualified type name |

## Examples

### Default Configuration

```csharp
.AddScheduler(scheduler => scheduler
    .AddMetadataCleanup()  // Cleans ManifestManager + MetadataCleanup metadata
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
- `ManifestManagerTrain` and `MetadataCleanupTrain` are always included in the cleanup whitelist by default — you don't need to add them manually.
- Train type names are matched against the `name` column in the metadata table (which stores the interface FullName — the canonical train name).
