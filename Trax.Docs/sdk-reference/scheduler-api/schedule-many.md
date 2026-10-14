---
title: ScheduleMany / ScheduleManyAsync
description: Reference for ScheduleMany and ScheduleManyAsync, which schedule many instances of a train in one transaction from ManifestItems, with prefix pruning.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 5
package: Trax.Scheduler
namespace:
  - Trax.Scheduler.Configuration
  - Trax.Scheduler.Services.TraxScheduler
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.InferredScheduling.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.BatchScheduling.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/ManifestItem.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/TraxScheduler/ITraxScheduler.cs
---

# ScheduleMany / ScheduleManyAsync

Schedules many manifests of one train from a collection, in one transaction, and can delete the manifests of an earlier batch that the collection no longer holds. `ScheduleMany` is on `SchedulerConfigurationBuilder`, for startup; `ScheduleManyAsync` is on `ITraxScheduler`, for runtime.

## Signatures

### SchedulerConfigurationBuilder, named

```csharp fragment
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


### SchedulerConfigurationBuilder, unnamed

```csharp fragment
public SchedulerConfigurationBuilder ScheduleMany<TTrain>(
    IEnumerable<ManifestItem> items,
    Schedule schedule,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class
```

Each `ManifestItem.Id` is used as the full external ID (no name prefix applied).

### ManifestItem

```csharp fragment
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
| `DependsOn` | `string?` | The external ID of the parent manifest this item depends on. Used by `IncludeMany` and `ThenIncludeMany`. See [Dependent Scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling). |

### SchedulerConfigurationBuilder, explicit type parameters

```csharp fragment
// Name-based
public SchedulerConfigurationBuilder ScheduleMany<TTrain, TInput, TOutput, TSource>(
    string name,
    IEnumerable<TSource> sources,
    Func<TSource, (string Suffix, TInput Input)> map,
    Schedule schedule,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties

// Explicit
public SchedulerConfigurationBuilder ScheduleMany<TTrain, TInput, TOutput, TSource>(
    IEnumerable<TSource> sources,
    Func<TSource, (string ExternalId, TInput Input)> map,
    Schedule schedule,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties
```

### ITraxScheduler

```csharp fragment
Task<IReadOnlyList<Manifest>> ScheduleManyAsync<TTrain, TInput, TOutput, TSource>(
    IEnumerable<TSource> sources,
    Func<TSource, (string ExternalId, TInput Input)> map,
    Schedule schedule,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null,
    CancellationToken ct = default
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties
```

## Parameters

### Type parameters of the ManifestItem forms

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TTrain` | `class` | The train interface type. Can implement `IServiceTrain<TInput, TOutput>` with any output type. The input type is inferred at configuration time. The output is discarded for background jobs. |

### Type parameters of the explicit and runtime forms

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TTrain` | `IServiceTrain<TInput, TOutput>` | The train interface type. All items in the batch execute the same train. Can have any output type; the output is discarded for background jobs. |
| `TInput` | `IManifestProperties` | The input type for the train. Each item in the batch can have different input data. |
| `TOutput` | _(none)_ | The output type of the train. Not constrained; any output type is accepted. The output is discarded when jobs complete. |
| `TSource` | _(none)_ | The type of elements in the source collection. Can be any type and is transformed into `(ExternalId, Input)` pairs by the `map` function. |

### Parameters of the ManifestItem forms

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | Yes (name-based) | _(none)_ | The batch name. Automatically derives `groupId` = `name`, `prunePrefix` = `"{name}-"`, and each external ID = `"{name}-{item.Id}"`. |
| `items` | `IEnumerable<ManifestItem>` | Yes | _(none)_ | The collection of items to create manifests from. Each item becomes one scheduled manifest. |
| `schedule` | `Schedule` | Yes | _(none)_ | The schedule definition applied to **all** manifests in the batch. Use [Every](/docs/sdk-reference/scheduler-api/scheduling-helpers) or [Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers) helpers. |
| `options` | `Action<ScheduleOptions>?` | No | `null` | Optional callback to configure all scheduling options. The name-based overload pre-sets `Group(name)` and `PrunePrefix("{name}-")` before invoking your callback. See [ScheduleOptions](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions). |

### Parameters of the explicit and runtime forms

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `name` | `string` | Yes (name-based) | _(none)_ | The batch name. Automatically derives `groupId` = `name`, `prunePrefix` = `"{name}-"`, and each external ID = `"{name}-{suffix}"`. |
| `sources` | `IEnumerable<TSource>` | Yes | _(none)_ | The collection of items to create manifests from. Each item becomes one scheduled manifest. |
| `map` | `Func<TSource, (string, TInput)>` | Yes | _(none)_ | A function that transforms each source item into an ID/suffix and input pair. |
| `schedule` | `Schedule` | Yes | _(none)_ | The schedule definition applied to **all** manifests in the batch. |
| `options` | `Action<ScheduleOptions>?` | No | `null` | Optional callback to configure all scheduling options. See [ScheduleOptions](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions). |
| `configureEach` | `Action<TSource, ManifestOptions>?` | No | `null` | Optional callback to set per-item manifest options. Receives both the source item and options, allowing per-item overrides of the base options set via `options`. |
| `ct` | `CancellationToken` | No | `default` | Cancellation token (runtime API only). |

## Returns

- **Startup**: `SchedulerConfigurationBuilder`, for continued fluent chaining.
- **Runtime**: `Task<IReadOnlyList<Manifest>>`, all created or updated manifest records.

## Behavior

- The `ManifestItem` forms take `TInput` from `TTrain`'s `IServiceTrain<TInput, TOutput>` interface and check each item's input against it when the scheduler is built. The output is unconstrained and discarded. The forms with explicit type parameters remain for compatibility, and take a `map` function and a `configureEach` callback.
- All manifests are created/updated in a **single database transaction**. If any manifest fails to save, the entire batch is rolled back.
- Pruning runs in a **separate database context** after the main transaction commits. A prune failure does not roll back the upserted manifests. The failure is logged as a warning and retried on the next cycle.
- The name-based overload prunes only manifests in its own group, so a batch named `sync` leaves a batch named `sync-users` alone. With an explicit `PrunePrefix`, `AddScheduler` fails at startup when one batch's prefix starts another's, unless the shorter one is a name-based batch in a different group. It also fails when a single `Schedule` falls inside a batch's prune: its external ID starts with the prefix and, for a name-based batch, it is in the batch's group. The prune matches by prefix and group only; it does not check which application owns a manifest. A manifest with a pending or running run is never pruned.
- The `configureEach` callback receives `Action<TSource, ManifestOptions>` (not `Action<ManifestOptions>` like `Schedule`), which lets you customize options based on the source item. Each item gets its own copy of every batch option, misfire policy, threshold and exclusion list included, so a change in `configureEach` affects that item only.
- The source collection is materialized (`.ToList()`) internally to avoid multiple enumeration.
- The group is configured via `.Group(...)` on `ScheduleOptions`. Per-group settings (MaxActiveJobs, Priority, IsEnabled) can be set from code or adjusted at runtime from the dashboard. See [Per-Group Dispatch Controls](/docs/scheduler/scheduling-options#per-group-dispatch-controls).
- `ScheduleMany` cannot be followed by `.ThenInclude()`. Use [IncludeMany](/docs/sdk-reference/scheduler-api/dependent-scheduling) (with `dependsOn`) instead for batch dependent scheduling.

## Examples

```csharp compile
var tables = new[] { "customers", "orders", "products" };

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .ScheduleMany<ISyncTableTrain>(
            "sync",
            tables.Select(table => new ManifestItem(table, new SyncTableInput { TableName = table })),
            Every.Minutes(5),
            options => options
                .Priority(10)
                .Group(group => group.MaxActiveJobs(5).Priority(20)))));
```

This declares `sync-customers`, `sync-orders` and `sync-products` in the group `sync`.

### With pruning (automatic stale cleanup)

The named form prunes with the prefix `"{name}-"`. Deployed with `partners` removed from the list,
the batch above deletes `sync-partners`, which starts with `sync-` and is in the `sync` group but is
no longer declared.

With explicit type parameters and per-item options:

```csharp compile
var tables = new[] { "customers", "orders", "products" };

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .ScheduleMany<ISyncTableTrain, SyncTableInput, Unit, string>(
            tables,
            table => ($"sync-{table}", new SyncTableInput { TableName = table }),
            Every.Minutes(5),
            options => options.Priority(10).PrunePrefix("sync-"),
            configureEach: (table, opts) =>
            {
                if (table == "orders")
                {
                    opts.MaxRetries = 10;
                    opts.Priority = 25;
                }
            })));
```

At runtime:

```csharp compile
public class TenantSyncService(ITraxScheduler scheduler)
{
    public Task<IReadOnlyList<Manifest>> SyncTenants(IEnumerable<Tenant> tenants) =>
        scheduler.ScheduleManyAsync<ISyncTenantTrain, SyncTenantInput, Unit, Tenant>(
            sources: tenants,
            map: tenant => (
                ExternalId: $"tenant-sync-{tenant.Id}",
                Input: new SyncTenantInput { TenantId = tenant.Id, Name = tenant.Name }),
            schedule: Every.Hours(1),
            options => options.PrunePrefix("tenant-sync-").Group("tenant-syncs"));
}
```

## See also

- [Schedule / ScheduleAsync](/docs/sdk-reference/scheduler-api/schedule), one manifest, and [ScheduleOptions](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions)
- [Dependent scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling), `IncludeMany` and `ThenIncludeMany`
- [Orphan manifest cleanup](/docs/scheduler/orphan-manifest-cleanup)
