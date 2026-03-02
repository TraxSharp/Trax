---
layout: default
title: ScheduleMany / ScheduleManyAsync
parent: Scheduler API
grand_parent: API Reference
nav_order: 4
---

# ScheduleMany / ScheduleManyAsync

Batch-schedules multiple instances of a train from a collection. All manifests are created or updated in a **single transaction**. Supports automatic cleanup of stale manifests via `prunePrefix`.

`ScheduleMany` is used at startup configuration time; `ScheduleManyAsync` is used at runtime via `IManifestScheduler`.

## Signatures

### Startup: Name-Based with ManifestItem (Recommended)

```csharp
public SchedulerConfigurationBuilder ScheduleMany<TTrain>(
    string name,
    IEnumerable<ManifestItem> items,
    Schedule schedule,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class
```

The `name` parameter automatically derives:
- **`groupId`** = `name`
- **`prunePrefix`** = `"{name}-"`
- **`externalId`** = `"{name}-{item.Id}"` for each item

Each `ManifestItem` contains the item's ID and input. The input type is inferred from `TTrain`'s `IServiceTrain<TInput, Unit>` interface and validated at configuration time.

### Startup: Unnamed with ManifestItem

```csharp
public SchedulerConfigurationBuilder ScheduleMany<TTrain>(
    IEnumerable<ManifestItem> items,
    Schedule schedule,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class
```

Each `ManifestItem.Id` is used as the full external ID (no name prefix applied).

### ManifestItem

```csharp
public sealed record ManifestItem(
    string Id,
    IManifestProperties Input,
    string? DependsOn = null
);
```

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | The item's identifier. In name-based overloads, this becomes the suffix (full external ID = `"{name}-{Id}"`). In unnamed overloads, this is the full external ID. |
| `Input` | `IManifestProperties` | The train input for this item. Must match the expected input type of `TTrain`. |
| `DependsOn` | `string?` | The external ID of the parent manifest this item depends on. Used by `IncludeMany` and `ThenIncludeMany`. See [Dependent Scheduling]({{ site.baseurl }}{% link api-reference/scheduler-api/dependent-scheduling.md %}). |

### Startup: Explicit Type Parameters (Legacy)

The three-type-parameter forms are still available for backward compatibility:

```csharp
// Name-based
public SchedulerConfigurationBuilder ScheduleMany<TTrain, TInput, TSource>(
    string name,
    IEnumerable<TSource> sources,
    Func<TSource, (string Suffix, TInput Input)> map,
    Schedule schedule,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null
)
    where TTrain : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties

// Explicit
public SchedulerConfigurationBuilder ScheduleMany<TTrain, TInput, TSource>(
    IEnumerable<TSource> sources,
    Func<TSource, (string ExternalId, TInput Input)> map,
    Schedule schedule,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null
)
    where TTrain : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties
```

### Runtime (IManifestScheduler)

```csharp
Task<IReadOnlyList<Manifest>> ScheduleManyAsync<TTrain, TInput, TSource>(
    IEnumerable<TSource> sources,
    Func<TSource, (string ExternalId, TInput Input)> map,
    Schedule schedule,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null,
    CancellationToken ct = default
)
    where TTrain : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties
```

## Type Parameters

### ManifestItem API (Recommended)

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TTrain` | `class` | The train interface type. Must implement `IServiceTrain<TInput, Unit>`. The input type is inferred at configuration time. |

### Legacy / Runtime API

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TTrain` | `IServiceTrain<TInput, Unit>` | The train interface type. All items in the batch execute the same train. |
| `TInput` | `IManifestProperties` | The input type for the train. Each item in the batch can have different input data. |
| `TSource` | — | The type of elements in the source collection. Can be any type — it is transformed into `(ExternalId, Input)` pairs by the `map` function. |

## Parameters

### ManifestItem API (Recommended)

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | Yes (name-based) | — | The batch name. Automatically derives `groupId` = `name`, `prunePrefix` = `"{name}-"`, and each external ID = `"{name}-{item.Id}"`. |
| `items` | `IEnumerable<ManifestItem>` | Yes | — | The collection of items to create manifests from. Each item becomes one scheduled manifest. |
| `schedule` | `Schedule` | Yes | — | The schedule definition applied to **all** manifests in the batch. Use [Every]({{ site.baseurl }}{% link api-reference/scheduler-api/scheduling-helpers.md %}) or [Cron]({{ site.baseurl }}{% link api-reference/scheduler-api/scheduling-helpers.md %}) helpers. |
| `options` | `Action<ScheduleOptions>?` | No | `null` | Optional callback to configure all scheduling options. The name-based overload pre-sets `Group(name)` and `PrunePrefix("{name}-")` before invoking your callback. See [ScheduleOptions]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions). |

### Legacy API Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | Yes (name-based) | — | The batch name. Automatically derives `groupId` = `name`, `prunePrefix` = `"{name}-"`, and each external ID = `"{name}-{suffix}"`. |
| `sources` | `IEnumerable<TSource>` | Yes | — | The collection of items to create manifests from. Each item becomes one scheduled manifest. |
| `map` | `Func<TSource, (string, TInput)>` | Yes | — | A function that transforms each source item into an ID/suffix and input pair. |
| `schedule` | `Schedule` | Yes | — | The schedule definition applied to **all** manifests in the batch. |
| `options` | `Action<ScheduleOptions>?` | No | `null` | Optional callback to configure all scheduling options. See [ScheduleOptions]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions). |
| `configureEach` | `Action<TSource, ManifestOptions>?` | No | `null` | Optional callback to set per-item manifest options. Receives both the source item and options, allowing per-item overrides of the base options set via `options`. |
| `ct` | `CancellationToken` | No | `default` | Cancellation token (runtime API only). |

## Returns

- **Startup**: `SchedulerConfigurationBuilder` — for continued fluent chaining.
- **Runtime**: `Task<IReadOnlyList<Manifest>>` — all created or updated manifest records.

## Examples

### Basic Batch Scheduling with ManifestItem (Recommended)

```csharp
var tables = new[] { "customers", "orders", "products" };

services.AddTrax.CoreEffects(options => options
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer()
        .ScheduleMany<ISyncTableTrain>(
            "sync",
            tables.Select(table => new ManifestItem(
                table,
                new SyncTableInput { TableName = table }
            )),
            Every.Minutes(5))
    )
);
// Creates: sync-customers, sync-orders, sync-products
// groupId: "sync", prunePrefix: "sync-"
```

Each `ManifestItem` contains the item's ID (used as the suffix in name-based overloads) and the train input. No `map` function needed — the data is already structured.

### Unnamed Batch Scheduling

```csharp
var tables = new[] { "customers", "orders", "products" };

scheduler.ScheduleMany<ISyncTableTrain>(
    tables.Select(table => new ManifestItem(
        $"sync-{table}",
        new SyncTableInput { TableName = table }
    )),
    Every.Minutes(5));
```

### With Pruning (Automatic Stale Cleanup)

The name-based overload includes pruning automatically (`prunePrefix: "{name}-"`):

```csharp
// If "partners" was in a previous deployment but removed from this list,
// its manifest ("sync-partners") will be deleted because it starts with
// "sync-" but wasn't included in the current batch.
var tables = new[] { "customers", "orders" };

scheduler.ScheduleMany<ISyncTableTrain>(
    "sync",
    tables.Select(table => new ManifestItem(
        table,
        new SyncTableInput { TableName = table }
    )),
    Every.Minutes(5));
```

### With Group Configuration

```csharp
scheduler.ScheduleMany<ISyncTableTrain>(
    "sync",
    tables.Select(table => new ManifestItem(
        table,
        new SyncTableInput { TableName = table }
    )),
    Every.Minutes(5),
    options => options
        .Priority(10)
        .Group(group => group
            .MaxActiveJobs(5)
            .Priority(20)));
```

### Legacy API with Map Function

The three-type-parameter form is still available and supports per-item configuration via `configureEach`:

```csharp
scheduler.ScheduleMany<ISyncTableTrain, SyncTableInput, string>(
    tables,
    table => ($"sync-{table}", new SyncTableInput { TableName = table }),
    Every.Minutes(5),
    options => options.Priority(10),
    configureEach: (table, opts) =>
    {
        if (table == "orders")
        {
            opts.MaxRetries = 10;
            opts.Priority = 25;
        }
    });
```

### Runtime Scheduling

```csharp
public class TenantSyncService(IManifestScheduler scheduler)
{
    public async Task SyncTenants(IEnumerable<Tenant> tenants)
    {
        var manifests = await scheduler.ScheduleManyAsync<ISyncTenantTrain, SyncTenantInput, Tenant>(
            sources: tenants,
            map: tenant => (
                ExternalId: $"tenant-sync-{tenant.Id}",
                Input: new SyncTenantInput { TenantId = tenant.Id, Name = tenant.Name }
            ),
            schedule: Every.Hours(1),
            options => options
                .PrunePrefix("tenant-sync-")
                .Group("tenant-syncs"));
    }
}
```

## Remarks

- All manifests are created/updated in a **single database transaction**. If any manifest fails to save, the entire batch is rolled back.
- Pruning is included in the same transaction — stale manifests are deleted atomically with the new batch.
- The `configureEach` callback receives `Action<TSource, ManifestOptions>` (not `Action<ManifestOptions>` like `Schedule`) — this lets you customize options based on the source item. It applies per-item overrides on top of the base options from `ScheduleOptions`.
- The source collection is materialized (`.ToList()`) internally to avoid multiple enumeration.
- The group is configured via `.Group(...)` on `ScheduleOptions`. Per-group settings (MaxActiveJobs, Priority, IsEnabled) can be set from code or adjusted at runtime from the dashboard. See [Per-Group Dispatch Controls]({{ site.baseurl }}{% link scheduler/scheduling-options.md %}#per-group-dispatch-controls).
- `ScheduleMany` cannot be followed by `.ThenInclude()` — use [IncludeMany]({{ site.baseurl }}{% link api-reference/scheduler-api/dependent-scheduling.md %}) (with `dependsOn`) instead for batch dependent scheduling.
