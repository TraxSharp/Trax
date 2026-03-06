---
layout: default
title: Dependent Trains
parent: Scheduling
nav_order: 5
---

# Dependent Trains

## The Problem

Some jobs only make sense after another job finishes. An ETL pipeline extracts data first, then transforms and loads it. A notification train runs after a report completes. You could schedule both on the same interval and hope the timing works out, but that's fragile—if the parent runs slow or retries, the dependent kicks off against stale data.

Dependent trains solve this. A manifest with `ScheduleType.Dependent` doesn't run on a timer. It runs when its parent's `LastSuccessfulRun` moves forward.

## How It Works

Each polling cycle, the `ManifestManagerTrain` evaluates dependent manifests separately from time-based ones. The logic is simple:

1. Find the parent manifest (via `DependsOnManifestId`)
2. If `parent.LastSuccessfulRun > dependent.LastSuccessfulRun`, queue the dependent
3. If the parent has never succeeded, or the dependent already ran after the parent's last success, skip it

That's it. No event bus, no callbacks. The existing polling loop picks up the change on its next cycle.

The same guards that apply to scheduled manifests apply here too: if the dependent has an active execution, a queued work entry, or a dead letter awaiting intervention, it won't be queued again.

## Startup Configuration: ThenInclude

Chain a dependent train after a `Schedule` call:

```csharp
services.AddTrax(trax => trax
    .AddScheduler(scheduler => scheduler
        .UseLocalWorkers()
        .Schedule<IExtractTrain>(
            "extract-data",
            new ExtractInput { Source = "api" },
            Every.Hours(1),
            options => options.Group("etl"))
        .ThenInclude<ITransformTrain>(
            "transform-data",
            new TransformInput { Format = "parquet" },
            options => options.Group("etl"))
    )
);
```

*SDK Reference: [Schedule]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule.md %}), [ThenInclude]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %})*

`ThenInclude` captures the previous call's external ID as the parent. No schedule parameter—dependent manifests don't have one.

Chaining works: `.Schedule(...).ThenInclude(...).ThenInclude(...)` creates A &rarr; B &rarr; C. Each `ThenInclude` depends on the one before it. If `extract` fails, neither downstream train fires.

## Fan-Out: Include

Sometimes one job needs to trigger multiple independent downstream jobs. An extract might feed both a transform pipeline and a validation step. `ThenInclude` can't express this — it always chains from the previous manifest, producing a linear pipeline.

`Include` solves this. It always branches from the **root** `Schedule`, not the cursor:

```csharp
scheduler
    .Schedule<IExtractTrain>(
        "extract", new ExtractInput(), Every.Hours(1),
        options => options.Group("etl"))
    // Both depend on Extract, not on each other
    .Include<ITransformTrain>(
        "transform", new TransformInput(),
        options => options.Group("etl"))
    .Include<IValidateTrain>(
        "validate", new ValidateInput(),
        options => options.Group("etl"));
```

*SDK Reference: [Include]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %})*

This creates: `extract` &rarr; `transform`, `extract` &rarr; `validate`. When extract succeeds, both transform and validate are queued independently. If transform fails, validate is unaffected.

### Mixing Include and ThenInclude

`Include` and `ThenInclude` compose naturally. The builder tracks two pointers: the **root** (set by `Schedule`) and the **cursor** (moved by every `ThenInclude` or `Include`). `Include` always parents from the root. `ThenInclude` always parents from the cursor.

*SDK Reference: [Dependent Scheduling — mixed fan-out and chaining]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %}) for the full code example.*

## Bulk Dependencies: IncludeMany

For batch jobs where each item in one batch depends on a corresponding item in another, use `IncludeMany` with `ManifestItem` after `ScheduleMany`. Each `ManifestItem` specifies its parent via the `DependsOn` property:

```csharp
scheduler
    .ScheduleMany<IExtractTrain>(
        "extract",
        Enumerable.Range(0, 100).Select(i => new ManifestItem(
            $"{i}",
            new ExtractInput { Partition = i }
        )),
        Every.Minutes(30))
    .IncludeMany<ILoadTrain>(
        "load",
        Enumerable.Range(0, 100).Select(i => new ManifestItem(
            $"{i}",
            new LoadInput { Partition = i },
            DependsOn: $"extract-{i}"
        )));
// Creates: extract-0..extract-99 (groupId: "extract", prunePrefix: "extract-")
//          load-0..load-99 (groupId: "load", prunePrefix: "load-")
```

*SDK Reference: [ScheduleMany]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule-many.md %}), [IncludeMany]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %})*

The `DependsOn` property on each `ManifestItem` specifies the parent's external ID. In this example, `load-0` depends on `extract-0`, `load-1` on `extract-1`, and so on. When `extract-42` succeeds, only `load-42` gets queued—the rest are unaffected.

The mapping is flexible. You aren't limited to 1:1—multiple dependents can point to the same parent. The name-based overloads automatically set `groupId` and `prunePrefix` from the `name` parameter. For deeper chaining (a third batch level), use `ThenIncludeMany`.

*SDK Reference: [IncludeMany / ThenIncludeMany]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %}) — all overloads including many-to-one and batch chaining examples.*

## Bulk Fan-Out: IncludeMany

When a single root manifest should trigger an entire batch of dependents, use `IncludeMany` without `DependsOn` — items automatically depend on the root `Schedule`:

```csharp
scheduler
    .Schedule<IExtractTrain>(
        "extract-all",
        new ExtractInput { Mode = "full" },
        Every.Hours(1),
        options => options.Group("extract"))
    .IncludeMany<ILoadTrain>(
        "load",
        Enumerable.Range(0, 10).Select(i => new ManifestItem(
            $"{i}",
            new LoadInput { Partition = i }
        )));
```

*SDK Reference: [IncludeMany]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %})*

All 10 `load-*` manifests depend on `extract-all`. No `DependsOn` needed — `IncludeMany` automatically parents every item from the root `Schedule`. The name `"load"` derives `groupId: "load"` and `prunePrefix: "load-"`.

## Runtime API

For jobs created at runtime rather than startup, use `ITraxScheduler.ScheduleDependentAsync` (single) or `ScheduleManyDependentAsync` (batch). Both use upsert semantics, same as their non-dependent counterparts. `ScheduleManyDependentAsync` runs in a single transaction.

*SDK Reference: [ScheduleDependentAsync / ScheduleManyDependentAsync]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %}) — signatures, parameters, and examples.*

## Cycle Detection

ManifestGroup dependencies must form a directed acyclic graph (DAG). Circular dependencies between groups—where group A depends on group B which depends back on group A, directly or transitively—would create a deadlock where no group can ever fire.

The scheduler validates this **at startup**. When `AddScheduler` builds the configuration, it derives group-level edges from the manifest-level `Schedule`/`ThenInclude`/`Include`/`ScheduleMany`/`ThenIncludeMany`/`IncludeMany` calls and runs a topological sort (Kahn's algorithm) over the group graph. If a cycle is detected, the application fails fast with an `InvalidOperationException` listing the groups involved:

```
Circular dependency detected among manifest groups: [group-a, group-b, group-c].
Manifest groups must form a directed acyclic graph (DAG).
```

Dependencies **within** a single group (two manifests in the same `groupId` where one depends on the other) are fine—only cross-group edges are checked, since within-group ordering is handled by the polling loop's parent/dependent evaluation.

The dashboard also visualizes the group dependency graph on the ManifestGroups page and each ManifestGroup detail page, making it easy to see the structure at a glance.

## Dormant Dependents

Standard dependents auto-fire whenever their parent succeeds. But sometimes the parent needs to decide at runtime *which* dependents fire and *with what input*. Dormant dependents solve this: they're declared in the fluent API like normal dependents (keeping the topology self-contained), but they never auto-fire. The parent train must explicitly activate them at runtime.

### When to Use

- **Delta imports**: A parent detects which tables changed, then activates only those children with partition-specific input
- **Conditional pipelines**: A parent inspects results and selectively triggers downstream work
- **Fan-out with runtime input**: The number of children is known at registration, but the input varies per execution

### Registration

Add `.Dormant()` to the options when declaring a dependent:

```csharp
scheduler
    .ScheduleMany<IDeltaImportTrain>(
        "delta",
        allTables.Select(table => new ManifestItem(
            $"{table}",
            DeltaImportRequest.Create(table)
        )),
        Every.Seconds(10),
        options: o => o.Group(group => group.MaxActiveJobs(4)))
    .IncludeMany<ICacheBronzeTrain>(
        "delta-bronze",
        allJobs.Select(item => new ManifestItem(
            $"{item.Table}-{item.Batch}",
            ExtractRequest.Default(item.Table, item.Batch),
            DependsOn: $"delta-{item.Table}"
        )),
        options: o => o.Dormant().Group(group => group.MaxActiveJobs(4)));
```

The `delta-bronze-*` manifests appear in the topology with `ScheduleType.DormantDependent`. The ManifestManager never auto-queues them—neither on a timer nor when their parent succeeds.

### Runtime Activation

Inject `IDormantDependentContext` into any step within the parent train:

```csharp
public class QueueDeltaBronzeTasks(IDormantDependentContext dormants)
    : Step<(NetSuiteTable Table, Dictionary<int, List<int>> Buckets), Unit>
{
    public override async Task<Unit> Run(
        (NetSuiteTable Table, Dictionary<int, List<int>> Buckets) input)
    {
        var activations = input.Buckets
            .Where(b => b.Value.Count > 0)
            .Select(b => (
                ExternalId: $"delta-bronze-{input.Table}-{b.Key}",
                Input: ExtractRequest.Create(input.Table, b.Key, b.Value)));

        await dormants.ActivateManyAsync<ICacheBronzeTrain, ExtractRequest>(activations);
        return Unit.Default;
    }
}
```

Each call creates a `WorkQueue` entry with the runtime-supplied input. The `DependentPriorityBoost` is applied automatically, and group capacity limits still apply at dispatch time.

### Scoping Rules

- The context is bound to the currently executing parent manifest. You can only activate dormant dependents that declare **this parent** as their `DependsOnManifestId`.
- Attempting to activate a dormant dependent that belongs to a different parent throws `InvalidOperationException`.
- Attempting to activate a manifest that isn't `ScheduleType.DormantDependent` throws `InvalidOperationException`.

### Concurrency Guards

If a dormant dependent already has a queued `WorkQueue` entry or an active execution (`Pending`/`InProgress` metadata), the activation is silently skipped with a warning log. This prevents duplicate work when the parent runs faster than its children can complete.

### SDK Reference

- [`IDormantDependentContext`]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %}#idormantdependentcontext) — `ActivateAsync` and `ActivateManyAsync` signatures
- [`.Dormant()`]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %}#dormant-option) — `ScheduleOptions` builder method

## Under the Hood

### Database

Dependent trains add one column to the `manifest` table:

```sql
ALTER TABLE trax.manifest
    ADD COLUMN depends_on_manifest_id int
    REFERENCES trax.manifest(id) ON DELETE SET NULL;
```

It's a self-referencing FK. If the parent manifest is deleted, the dependent's `DependsOnManifestId` is set to `NULL`—it won't fire, but it won't break either.

The `schedule_type` enum has two dependency values: `dependent` and `dormant_dependent`. Both have no `CronExpression` or `IntervalSeconds`—those fields are `NULL`. The difference is behavioral: `dependent` manifests auto-fire when their parent succeeds, while `dormant_dependent` manifests must be explicitly activated via `IDormantDependentContext`.

### Evaluation in ManifestManagerTrain

The `DetermineJobsToQueueStep` runs two passes:

1. **Time-based manifests** (Cron, Interval): checked against their schedule as before. `DormantDependent` manifests are explicitly excluded from this pass.
2. **Dependent manifests**: only `ScheduleType.Dependent` manifests are checked against their parent's `LastSuccessfulRun`. `DormantDependent` manifests are excluded—they are never auto-queued.

The dependent pass loads all enabled manifests (parents and dependents together), so it can resolve parent references without extra queries. If a parent is disabled or missing from the loaded set, its dependents are skipped.

### Chain Behavior

In an A &rarr; B &rarr; C chain, B won't fire until A succeeds. C won't fire until B succeeds. If A succeeds but B fails and gets dead-lettered, C stays idle—it's still waiting for B's `LastSuccessfulRun` to advance.

Each link in the chain is independent. The scheduler doesn't have a concept of "the whole chain failed." Each manifest manages its own retries and dead letters. This keeps the model simple: every manifest is still just a manifest, whether it runs on a timer or after another manifest.

### What Happens When...

| Scenario | Result |
|----------|--------|
| Parent succeeds, dependent never ran | Dependent queued |
| Parent succeeds, dependent already ran after parent's last success | Dependent skipped |
| Parent never succeeded | Dependent skipped |
| Parent disabled | Dependent skipped (parent not in loaded set) |
| Dependent has a dead letter | Dependent skipped until resolved |
| Dependent has an active execution | Dependent skipped (no double-queue) |
| Parent deleted | `DependsOnManifestId` set to NULL, dependent skipped |
| Parent succeeds, dormant dependent exists | Dormant dependent **not** queued (requires explicit activation) |
| Parent activates dormant dependent via `IDormantDependentContext` | WorkQueue entry created with runtime input |
| Parent activates dormant dependent that is already queued/active | Activation silently skipped |
