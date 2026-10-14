---
title: Dependent Trains
description: "How dependent manifests run when a parent succeeds: Include and ThenInclude, when a dependent is queued, cycle detection and dormant dependents."
source:
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Junctions/DetermineJobsToQueueJunction.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/DormantDependentContext
type: explanation
area: scheduling
nav_order: 7
---

# Dependent Trains

## The Problem

Some jobs only make sense after another job finishes. An ETL pipeline extracts data first, then transforms and loads it. A notification train runs after a report completes. You could schedule both on the same interval and hope the timing works out, but that's fragile, if the parent runs slow or retries, the dependent kicks off against stale data.

Dependent trains solve this. A manifest with `ScheduleType.Dependent` doesn't run on a timer. It runs when its parent's `LastSuccessfulRun` moves forward.

## How It Works

Each polling cycle, the `ManifestManagerTrain` evaluates dependent manifests separately from time-based ones. The logic is simple:

1. Find the parent manifest (via `DependsOnManifestId`)
2. If the parent's `LastSuccessfulRun` is later than the moment the dependent's latest run **started** (successful or cancelled), queue the dependent
3. If the parent has never succeeded, or the dependent's latest run started after the parent's last success, skip it

A dependent runs **at least once after its parent's latest success**. Comparing against when the dependent's run started, rather than when it finished, is what makes that hold: a parent success that lands while the dependent is running was not seen by that run, so the dependent is queued once more when it finishes. Several parent successes that land during one dependent run collapse into that single re-run, which reads the parent's latest output, the output the earlier successes produced too. A dependent run is not queued per parent success.

A dependent run that was cancelled (timed out, or cancelled by an operator) counts from its start like a successful one: it consumed the parent success it was started for, but not one that landed while it ran, so that one still earns a run.

Both times come from the database's clock, not from the machines involved. The parent's `LastSuccessfulRun` is stamped by the database when the worker records the success, and the dependent's run is dated by its work queue entry's `DispatchedAt`, which the database stamps when the dispatcher claims it. A worker and a dispatcher whose clocks disagree therefore cannot make a finished dependent look older than its parent's success and queue it again after every run. A dependent run with no dispatched entry (run directly, or dispatched by the in-memory provider) falls back to its own start time, and with no run on record at all (its history was pruned) the dependent's own `LastSuccessfulRun` stands in.

That's it. No event bus, no callbacks. The existing polling loop picks up the change on its next cycle.

The same guards that apply to scheduled manifests apply here too: if the dependent has an active execution, a queued work entry, or a dead letter awaiting intervention, it won't be queued again.

## Declaring dependents

A dependent is declared next to its parent, and has no schedule of its own:

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler
                .Schedule<IExtractTrain>(
                    "extract",
                    new ExtractInput { Source = "api" },
                    Every.Hours(1),
                    options => options.Group("etl")
                )
                // Both depend on extract, not on each other
                .Include<ITransformTrain>(
                    "transform",
                    new TransformInput { Format = "parquet" },
                    options => options.Group("etl")
                )
                .Include<IValidateTrain>(
                    "validate",
                    new ValidateInput(),
                    options => options.Group("etl")
                )
                // Depends on validate, the manifest before it
                .ThenInclude<ILoadTrain>("load", new LoadInput(), options => options.Group("etl"))
        )
);
```

The builder keeps two pointers. The **root** is set by `Schedule`, and `Include` always parents
from it, so `Include` fans out: `extract` feeds `transform` and `validate` independently, and a
failed `transform` does not hold up `validate`. The **cursor** moves with every `Include` or
`ThenInclude`, and `ThenInclude` parents from it, so `ThenInclude` chains: A, then B, then C, and
if A fails nothing downstream fires.

Batches work the same way. `IncludeMany` after `ScheduleMany` gives each item a parent through its
`ManifestItem.DependsOn` (so `load-42` can depend on `extract-42` alone), or parents every item from
the root `Schedule` when `DependsOn` is left out. `ThenIncludeMany` chains a third level. At
runtime, `ITraxScheduler.ScheduleDependentAsync` and `ScheduleManyDependentAsync` do the same with
upsert semantics, the batch in one transaction. Every form, with examples, is on
[Dependent Scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling).

## Cycle Detection

ManifestGroup dependencies must form a directed acyclic graph (DAG). Circular dependencies between groups, where group A depends on group B which depends back on group A, directly or transitively, would create a deadlock where no group can ever fire.

The scheduler validates this **at startup**. When `AddScheduler` builds the configuration, it derives group-level edges from the manifest-level `Schedule`/`ThenInclude`/`Include`/`ScheduleMany`/`ThenIncludeMany`/`IncludeMany` calls and runs a topological sort (Kahn's algorithm) over the group graph. If a cycle is detected, the application fails fast with an `InvalidOperationException` listing the groups involved:

```
Circular dependency detected among manifest groups: [group-a, group-b, group-c].
Manifest groups must form a directed acyclic graph (DAG).
```

Dependencies **within** a single group (two manifests in the same `groupId` where one depends on the other) are fine, only cross-group edges are checked, since within-group ordering is handled by the polling loop's parent/dependent evaluation.

The dashboard also visualizes the group dependency graph on the ManifestGroups page and each ManifestGroup detail page, making it easy to see the structure at a glance.

## Dormant Dependents

Standard dependents auto-fire whenever their parent succeeds. But sometimes the parent needs to decide at runtime *which* dependents fire and *with what input*. Dormant dependents solve this: they're declared in the fluent API like normal dependents (keeping the topology self-contained), but they never auto-fire. The parent train must explicitly activate them at runtime.

### When to Use

- **Delta imports**: A parent detects which tables changed, then activates only those children with partition-specific input
- **Conditional pipelines**: A parent inspects results and selectively triggers downstream work
- **Fan-out with runtime input**: The number of children is known at registration, but the input varies per execution

### Declaring and activating them

A dormant dependent is declared like any other, with `.Dormant()` in its options, so the topology
stays in one place. The manifests get `ScheduleType.DormantDependent`, and the ManifestManager never
queues them, on a timer or when the parent succeeds. A junction in the parent train injects
`IDormantDependentContext` and calls `ActivateAsync` or `ActivateManyAsync` with the external ids
to fire and the input for each. Each activation writes a work queue entry with that input; the
`DependentPriorityBoost` is applied, and group capacity limits still apply at dispatch. Both are
shown on [Dependent Scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling).

### Scoping Rules

- The context is bound to the currently executing parent manifest. You can only activate dormant dependents that declare **this parent** as their `DependsOnManifestId`.
- Attempting to activate a dormant dependent that belongs to a different parent throws `InvalidOperationException`.
- Attempting to activate a manifest that isn't `ScheduleType.DormantDependent` throws `InvalidOperationException`.

### Concurrency Guards

If a dormant dependent is disabled, or its manifest group is, the activation is skipped with a warning log: a disabled manifest stays stopped until it is re-enabled.

If a dormant dependent already has a queued `WorkQueue` entry or an active execution (`Pending`/`InProgress` metadata), the activation is skipped with a warning log. This prevents duplicate work when the parent runs faster than its children can complete. Unlike a standard dependent, a dormant dependent is not queued again afterwards: the skipped activation's input is dropped, so a parent that must not lose one should activate again on its next run.

## Under the Hood

### Database

Dependent trains add one column to the `manifest` table:

```sql
ALTER TABLE trax.manifest
    ADD COLUMN depends_on_manifest_id int
    REFERENCES trax.manifest(id) ON DELETE SET NULL;
```

It's a self-referencing FK. If the parent manifest is deleted, the dependent's `DependsOnManifestId` is set to `NULL`, it won't fire, but it won't break either.

The `schedule_type` enum has two dependency values: `dependent` and `dormant_dependent`. Both have no `CronExpression` or `IntervalSeconds`, those fields are `NULL`. The difference is behavioral: `dependent` manifests auto-fire when their parent succeeds, while `dormant_dependent` manifests must be explicitly activated via `IDormantDependentContext`.

### Evaluation in ManifestManagerTrain

The `DetermineJobsToQueueJunction` runs two passes:

1. **Time-based manifests** (Cron, Interval): checked against their schedule as before. `DormantDependent` manifests are explicitly excluded from this pass.
2. **Dependent manifests**: only `ScheduleType.Dependent` manifests are checked against their parent's `LastSuccessfulRun`. `DormantDependent` manifests are excluded, they are never auto-queued.

The dependent pass loads all enabled manifests (parents and dependents together), so it can resolve parent references without extra queries. If a parent is disabled or missing from the loaded set, its dependents are skipped.

### Chain Behavior

In an A &rarr; B &rarr; C chain, B won't fire until A succeeds. C won't fire until B succeeds. If A succeeds but B fails and gets dead-lettered, C stays idle, it's still waiting for B's `LastSuccessfulRun` to advance.

Each link in the chain is independent. The scheduler doesn't have a concept of "the whole chain failed." Each manifest manages its own retries and dead letters. This keeps the model simple: every manifest is still just a manifest, whether it runs on a timer or after another manifest.

### What Happens When...

| Scenario | Result |
|----------|--------|
| Parent succeeds, dependent never ran | Dependent queued |
| Parent succeeds, dependent's latest run started after that | Dependent skipped |
| Parent succeeds while the dependent is running | Dependent queued again once that run finishes |
| Parent succeeds three times during one dependent run | Dependent queued once, and reads the parent's latest output |
| Parent succeeds during a dependent run that is then cancelled | Dependent queued again |
| Parent never succeeded | Dependent skipped |
| Parent disabled | Dependent skipped (parent not in loaded set) |
| Dependent has a dead letter | Dependent skipped until resolved |
| Dependent has an active execution | Dependent skipped (no double-queue) |
| Parent deleted | `DependsOnManifestId` set to NULL, dependent skipped |
| Parent succeeds, dormant dependent exists | Dormant dependent **not** queued (requires explicit activation) |
| Parent activates dormant dependent via `IDormantDependentContext` | WorkQueue entry created with runtime input |
| Parent activates dormant dependent that is already queued/active | Activation skipped with a warning |
| Parent activates dormant dependent that is disabled, or in a disabled group | Activation skipped with a warning |

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [ThenInclude / Include](/docs/sdk-reference/scheduler-api/dependent-scheduling) | [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many) | [IDormantDependentContext](/docs/sdk-reference/scheduler-api/i-dormant-dependent-context) | [.Dormant()](/docs/sdk-reference/scheduler-api/dependent-scheduling#dormant-option)
