---
layout: default
title: Dependent Scheduling
parent: Scheduler API
grand_parent: API Reference
nav_order: 5
---

# Dependent Scheduling

Schedules workflows that run only after a parent manifest completes successfully. There are three patterns for declaring dependencies at startup:

- **Root-based** (`Include` / `IncludeMany`) — branches from the root `Schedule` or explicitly maps parents via `dependsOn`. Use `IncludeMany` for first-level batch dependents after `ScheduleMany`.
- **Cursor-based** (`ThenInclude` / `ThenIncludeMany`) — chains from the most recently declared manifest, creating deeper pipelines. Use `ThenIncludeMany` for second-level-and-beyond batch dependents after a previous `IncludeMany`.

At runtime, `ScheduleDependentAsync` and `ScheduleManyDependentAsync` take an explicit parent external ID.

Dependent manifests are evaluated during polling — when a parent's `LastSuccessfulRun` is newer than the dependent's own `LastSuccessfulRun`, the dependent is queued for execution.

## Signatures

### Startup: ThenInclude (Single — cursor-based, Recommended)

```csharp
public SchedulerConfigurationBuilder ThenInclude<TWorkflow>(
    string externalId,
    IManifestProperties input,
    Action<ScheduleOptions>? options = null
)
    where TWorkflow : class
```

### Startup: Include (Single — root-based, Recommended)

```csharp
public SchedulerConfigurationBuilder Include<TWorkflow>(
    string externalId,
    IManifestProperties input,
    Action<ScheduleOptions>? options = null
)
    where TWorkflow : class
```

Both infer the input type from `TWorkflow`'s `IServiceTrain<TInput, Unit>` interface and validate the provided `input` at configuration time.

### Startup: IncludeMany with ManifestItem (Recommended)

```csharp
// Name-based: derives groupId, prunePrefix, and external IDs from name
public SchedulerConfigurationBuilder IncludeMany<TWorkflow>(
    string name,
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TWorkflow : class

// Unnamed: each ManifestItem.Id is the full external ID
public SchedulerConfigurationBuilder IncludeMany<TWorkflow>(
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TWorkflow : class
```

Each item's `ManifestItem.DependsOn` specifies the parent's external ID. When `DependsOn` is null, the item falls back to the root `Schedule`. If all items have explicit `DependsOn`, no preceding `Schedule` is required (useful after `ScheduleMany`).

The `name` parameter automatically derives `groupId` = `name`, `prunePrefix` = `"{name}-"`, and each external ID = `"{name}-{item.Id}"`.

### Startup: ThenIncludeMany with ManifestItem (Recommended)

```csharp
// Name-based
public SchedulerConfigurationBuilder ThenIncludeMany<TWorkflow>(
    string name,
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TWorkflow : class

// Unnamed
public SchedulerConfigurationBuilder ThenIncludeMany<TWorkflow>(
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TWorkflow : class
```

Every `ManifestItem.DependsOn` **must** be set — `ThenIncludeMany` throws `InvalidOperationException` if any item has a null `DependsOn`.

### Startup: Explicit Type Parameters (Legacy)

The two-type-parameter single forms and three-type-parameter batch forms are still available for backward compatibility:

```csharp
// Single
public SchedulerConfigurationBuilder ThenInclude<TWorkflow, TInput>(...)
    where TWorkflow : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties

public SchedulerConfigurationBuilder Include<TWorkflow, TInput>(...)
    where TWorkflow : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties

// Batch (with map + dependsOn functions)
public SchedulerConfigurationBuilder IncludeMany<TWorkflow, TInput, TSource>(...)
public SchedulerConfigurationBuilder ThenIncludeMany<TWorkflow, TInput, TSource>(...)
```

### Runtime: ScheduleDependentAsync (Single)

```csharp
Task<Manifest> ScheduleDependentAsync<TWorkflow, TInput>(
    string externalId,
    TInput input,
    string dependsOnExternalId,
    Action<ScheduleOptions>? options = null,
    CancellationToken ct = default
)
    where TWorkflow : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties
```

### Runtime: ScheduleManyDependentAsync (Batch)

```csharp
Task<IReadOnlyList<Manifest>> ScheduleManyDependentAsync<TWorkflow, TInput, TSource>(
    IEnumerable<TSource> sources,
    Func<TSource, (string ExternalId, TInput Input)> map,
    Func<TSource, string> dependsOn,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null,
    CancellationToken ct = default
)
    where TWorkflow : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties
```

## Parameters

### ThenInclude / Include (Startup — Single)

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | Unique identifier for this dependent job |
| `input` | `IManifestProperties` (inferred) / `TInput` (explicit) | Yes | Input data passed to the workflow on each execution. Validated against the workflow's expected input type at configuration time. |
| `options` | `Action<ScheduleOptions>?` | No | Optional callback to configure all scheduling options via a fluent builder. Includes manifest-level settings (`Priority`, `Enabled`, `MaxRetries`, `Timeout`) and group-level settings (`.Group(...)` with `MaxActiveJobs`, `Priority`, `Enabled`). See [ScheduleOptions]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions). |

`ThenInclude` links to the **cursor** — the most recently declared manifest (the last `Schedule`, `ThenInclude`, or `Include`). Must be called after `Schedule()`, `Include()`, or another `ThenInclude()`.

`Include` links to the **root** — the most recent `Schedule()` call. Must be called after `Schedule()`. Use `Include` to create multiple independent branches from a single root.

### IncludeMany / ThenIncludeMany with ManifestItem (Startup — Batch)

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `name` | `string` | No (name-based only) | The batch name. Derives `groupId`, `prunePrefix`, and external ID prefix. |
| `items` | `IEnumerable<ManifestItem>` | Yes | Items to create as dependent manifests. Each item's `DependsOn` specifies the parent. |
| `options` | `Action<ScheduleOptions>?` | No | Optional callback to configure scheduling options. |

`IncludeMany` items can use `DependsOn` per-item or fall back to the root `Schedule`. `ThenIncludeMany` requires `DependsOn` on every item.

### ScheduleDependentAsync (Runtime — Single)

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | Unique identifier for this dependent job |
| `input` | `TInput` | Yes | Input data passed to the workflow on each execution |
| `dependsOnExternalId` | `string` | Yes | The `ExternalId` of the parent manifest this job depends on |
| `options` | `Action<ScheduleOptions>?` | No | Optional callback to configure all scheduling options via a fluent builder. See [ScheduleOptions]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}#scheduleoptions). |
| `ct` | `CancellationToken` | No | Cancellation token |

### ScheduleManyDependentAsync (Runtime — Batch)

Uses the legacy three-type-parameter API with `map` and `dependsOn` functions. See [ScheduleMany]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %}) for parameter details.

## Examples

### Chained Dependencies (A &rarr; B &rarr; C)

```csharp
services.AddTrax.CoreEffects(options => options
    .AddScheduler(scheduler => scheduler
        .UsePostgresTaskServer()
        // A: Extract runs every 5 minutes
        .Schedule<IExtractWorkflow>(
            "etl-extract",
            new ExtractInput(),
            Every.Minutes(5),
            options => options.Group("etl-pipeline"))
        // B: Transform runs after Extract succeeds (priority 5 + DependentPriorityBoost)
        .ThenInclude<ITransformWorkflow>(
            "etl-transform",
            new TransformInput(),
            options => options
                .Group("etl-pipeline")
                .Priority(5))
        // C: Load runs after Transform succeeds
        .ThenInclude<ILoadWorkflow>(
            "etl-load",
            new LoadInput(),
            options => options.Group("etl-pipeline"))
    )
);
```

### Fan-Out Dependencies (A &rarr; B, A &rarr; C)

Use `Include` to create multiple branches from a single root. Each `Include` depends on the root `Schedule`, not the previous call:

```csharp
scheduler
    .Schedule<IExtractWorkflow>(
        "extract", new ExtractInput(), Every.Hours(1),
        options => options.Group("etl"))
    // Both Transform and Validate depend on Extract (fan-out)
    .Include<ITransformWorkflow>(
        "transform", new TransformInput(),
        options => options.Group("etl"))
    .Include<IValidateWorkflow>(
        "validate", new ValidateInput(),
        options => options.Group("etl"))
```

### Mixed Fan-Out and Chaining

`Include` and `ThenInclude` can be combined. `Include` branches from the root, `ThenInclude` chains from the cursor:

```csharp
scheduler
    .Schedule<IExtractWorkflow>(
        "extract", new ExtractInput(), Every.Hours(1),
        options => options.Group("etl"))
    // Branch 1: Extract → Transform → Load
    .Include<ITransformWorkflow>(
        "transform", new TransformInput(),
        options => options.Group("etl"))
        .ThenInclude<ILoadWorkflow>(
            "load", new LoadInput(),
            options => options.Group("etl"))
    // Branch 2: Extract → Validate (back to root)
    .Include<IValidateWorkflow>(
        "validate", new ValidateInput(),
        options => options.Group("etl"))
```

Result: `Extract → Transform → Load`, `Extract → Validate`

### Batch Dependent Scheduling with ManifestItem

```csharp
scheduler
    .ScheduleMany<IExtractWorkflow>(
        "extract",
        Enumerable.Range(0, 10).Select(i => new ManifestItem(
            $"{i}",
            new ExtractInput { Index = i }
        )),
        Every.Minutes(5))
    .IncludeMany<ITransformWorkflow>(
        "transform",
        Enumerable.Range(0, 10).Select(i => new ManifestItem(
            $"{i}",
            new TransformInput { Index = i },
            DependsOn: $"extract-{i}"
        )));
// Creates: extract-0..extract-9, transform-0..transform-9
```

Each `ManifestItem` specifies its parent via the `DependsOn` property. No separate `dependsOn` function needed.

### Batch Fan-Out (IncludeMany)

All items in the batch depend on a single root `Schedule`:

```csharp
scheduler
    .Schedule<IExtractWorkflow>(
        "extract-all", new ExtractInput(), Every.Hours(1),
        options => options.Group("extract"))
    .IncludeMany<ILoadWorkflow>(
        Enumerable.Range(0, 10).Select(i => new ManifestItem(
            $"load-{i}",
            new LoadInput { Partition = i }
        )))
```

All 10 `load-*` manifests depend on `extract-all`. When `ManifestItem.DependsOn` is null, it falls back to the root `Schedule`.

### Runtime Dependent Scheduling

```csharp
// Create parent
await scheduler.ScheduleAsync<IFetchDataWorkflow, FetchInput>(
    "fetch-data", new FetchInput(), Cron.Hourly());

// Create dependent
await scheduler.ScheduleDependentAsync<IProcessDataWorkflow, ProcessInput>(
    externalId: "process-data",
    input: new ProcessInput(),
    dependsOnExternalId: "fetch-data");
```

## Dormant Option

Add `.Dormant()` to `ScheduleOptions` when declaring a dependent to make it a dormant dependent. Dormant dependents appear in the topology but never auto-fire—they must be explicitly activated at runtime by the parent workflow.

```csharp
scheduler
    .Schedule<IParentWorkflow>(
        "parent", new ParentInput(), Every.Minutes(5))
    .Include<IChildWorkflow>(
        "child", new ChildInput(),
        options: o => o.Dormant());
```

The manifest is created with `ScheduleType.DormantDependent` instead of `ScheduleType.Dependent`. The ManifestManager excludes it from both time-based and dependent evaluation.

## IDormantDependentContext

A scoped service for activating dormant dependent manifests at runtime. Injected into workflow steps that need to selectively fire dependent workflows with runtime-determined input.

The context is automatically initialized by the `TaskServerExecutor` before the user's workflow runs. Only dormant dependents declared as children of the currently executing parent manifest can be activated.

### ActivateAsync

```csharp
Task ActivateAsync<TWorkflow, TInput>(
    string externalId,
    TInput input,
    CancellationToken ct = default
)
    where TWorkflow : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `string` | Yes | The external ID of the dormant dependent manifest to activate |
| `input` | `TInput` | Yes | The runtime-determined input for the dependent workflow |
| `ct` | `CancellationToken` | No | Cancellation token |

**Exceptions:**
- `InvalidOperationException` — context not initialized, manifest not found, manifest is not `DormantDependent`, or manifest does not depend on the current parent

**Concurrency:** If the target manifest already has a queued `WorkQueue` entry or an active execution (`Pending`/`InProgress` metadata), the activation is silently skipped with a warning log.

### ActivateManyAsync

```csharp
Task ActivateManyAsync<TWorkflow, TInput>(
    IEnumerable<(string ExternalId, TInput Input)> activations,
    CancellationToken ct = default
)
    where TWorkflow : IServiceTrain<TInput, Unit>
    where TInput : IManifestProperties
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `activations` | `IEnumerable<(string, TInput)>` | Yes | Collection of (ExternalId, Input) pairs to activate |
| `ct` | `CancellationToken` | No | Cancellation token |

All activations are performed in a single database transaction. If any validation fails (wrong parent, not dormant, etc.), the entire batch is rolled back. Concurrency-skipped entries (already queued/active) do not cause a rollback.

### Example

```csharp
public class SelectiveDispatchStep(IDormantDependentContext dormants)
    : Step<DispatchInput, Unit>
{
    public override async Task<Unit> Run(DispatchInput input)
    {
        // Single activation
        await dormants.ActivateAsync<IChildWorkflow, ChildInput>(
            "child-1",
            new ChildInput { Data = input.RuntimeData });

        // Batch activation
        var activations = input.Items
            .Select(item => ($"child-{item.Id}", new ChildInput { Data = item.Data }));
        await dormants.ActivateManyAsync<IChildWorkflow, ChildInput>(activations);

        return Unit.Default;
    }
}
```

## Remarks

- `ThenInclude()` must follow `Schedule()`, `Include()`, or another `ThenInclude()` — calling it first throws `InvalidOperationException`.
- `Include()` and `IncludeMany()` (without `dependsOn`) must follow `Schedule()` — calling them without a root throws `InvalidOperationException`.
- `IncludeMany()` (with `dependsOn`) can follow `ScheduleMany()` for first-level batch dependents. `ThenIncludeMany()` is for deeper chaining after a previous `IncludeMany()`.
- Dependent manifests have `ScheduleType.Dependent` and no interval/cron schedule of their own — they are triggered solely by their parent's successful completion. Dormant dependents have `ScheduleType.DormantDependent` and must be explicitly activated via `IDormantDependentContext`.
- The dependency check compares `parent.LastSuccessfulRun > dependent.LastSuccessfulRun` during each polling cycle.
- **Cursor vs. Root**: The builder tracks two pointers — the *cursor* (last declared manifest, used by `ThenInclude`) and the *root* (the last `Schedule()`, used by `Include`). `Schedule` sets both. `ThenInclude` and `Include` move the cursor but leave the root unchanged. `ScheduleMany` resets both to null.
- **Priority boost**: When a dependent manifest's work queue entry is created, `DependentPriorityBoost` (default 16) is added to its base priority. This ensures dependent workflows are dispatched before non-dependent workflows by default. The boost is configurable via [`DependentPriorityBoost`]({{ site.baseurl }}{% link api-reference/scheduler-api/add-scheduler.md %}) on the scheduler builder. The final priority is clamped to [0, 31].
- **Cycle detection**: ManifestGroup dependencies must form a DAG. At startup, the builder derives group-level edges from all `Schedule`/`ThenInclude`/`Include`/`ScheduleMany`/`ThenIncludeMany`/`IncludeMany` calls and validates that no circular dependencies exist between groups. If a cycle is detected, `Build()` throws `InvalidOperationException` listing the groups involved. Within-group dependencies are allowed—only cross-group edges are validated. See [Dependent Workflows — Cycle Detection]({{ site.baseurl }}{% link scheduler/dependent-workflows.md %}#cycle-detection) for details.
