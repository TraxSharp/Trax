---
title: Dependent scheduling
description: Reference for ThenInclude, Include, IncludeMany, ThenIncludeMany, ScheduleDependentAsync and ScheduleManyDependentAsync, which schedule dependent manifests.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 6
package: Trax.Scheduler
namespace:
  - Trax.Scheduler.Configuration
  - Trax.Scheduler.Services.TraxScheduler
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.InferredScheduling.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.Scheduling.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.BatchScheduling.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/TraxScheduler/ITraxScheduler.cs
---

# Dependent scheduling

The methods that schedule a manifest to run after a parent manifest succeeds instead of on a
schedule of its own. At startup they are on `SchedulerConfigurationBuilder`: `ThenInclude` and
`ThenIncludeMany` depend on the manifest declared just before (the cursor), `Include` and
`IncludeMany` on the last `Schedule` (the root) or on a parent each item names. At runtime they are
on [ITraxScheduler](/docs/sdk-reference/scheduler-api/i-trax-scheduler): `ScheduleDependentAsync`
and `ScheduleManyDependentAsync`, which name the parent explicitly.

## Signatures

### SchedulerConfigurationBuilder

```csharp fragment
public SchedulerConfigurationBuilder ThenInclude<TTrain>(
    string externalId,
    IManifestProperties input,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class

public SchedulerConfigurationBuilder Include<TTrain>(
    string externalId,
    IManifestProperties input,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class

public SchedulerConfigurationBuilder IncludeMany<TTrain>(
    string name,
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class

public SchedulerConfigurationBuilder IncludeMany<TTrain>(
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class

public SchedulerConfigurationBuilder ThenIncludeMany<TTrain>(
    string name,
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class

public SchedulerConfigurationBuilder ThenIncludeMany<TTrain>(
    IEnumerable<ManifestItem> items,
    Action<ScheduleOptions>? options = null
)
    where TTrain : class

// The forms with explicit type parameters
public SchedulerConfigurationBuilder ThenInclude<TTrain, TInput, TOutput>(string externalId, TInput input, Action<ScheduleOptions>? options = null)
public SchedulerConfigurationBuilder Include<TTrain, TInput, TOutput>(string externalId, TInput input, Action<ScheduleOptions>? options = null)
public SchedulerConfigurationBuilder IncludeMany<TTrain, TInput, TOutput, TSource>(...)
public SchedulerConfigurationBuilder ThenIncludeMany<TTrain, TInput, TOutput, TSource>(...)
```

### ITraxScheduler

```csharp fragment
Task<Manifest> ScheduleDependentAsync<TTrain, TInput, TOutput>(
    string externalId,
    TInput input,
    string dependsOnExternalId,
    Action<ScheduleOptions>? options = null,
    CancellationToken ct = default
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties

Task<IReadOnlyList<Manifest>> ScheduleManyDependentAsync<TTrain, TInput, TOutput, TSource>(
    IEnumerable<TSource> sources,
    Func<TSource, (string ExternalId, TInput Input)> map,
    Func<TSource, string> dependsOn,
    Action<ScheduleOptions>? options = null,
    Action<TSource, ManifestOptions>? configureEach = null,
    CancellationToken ct = default
)
    where TTrain : IServiceTrain<TInput, TOutput>
    where TInput : IManifestProperties
```

## Parameters

| Parameter | Used by | Description |
|-----------|---------|-------------|
| `externalId` | `ThenInclude`, `Include`, `ScheduleDependentAsync` | The dependent manifest's external id, its upsert key |
| `input` | `ThenInclude`, `Include`, `ScheduleDependentAsync` | The input the dependent runs with. In the inferred forms it is checked against the train's input type when the scheduler is built |
| `name` | `IncludeMany`, `ThenIncludeMany` (named) | The batch name. The group id is `name`, the prune prefix `"{name}-"`, and each external id `"{name}-{item.Id}"` |
| `items` | `IncludeMany`, `ThenIncludeMany` | The dependents, as [`ManifestItem`](/docs/sdk-reference/scheduler-api/schedule-many#manifestitem) records; each item's `DependsOn` names its parent's external id. In the unnamed forms each `Id` is the full external id |
| `dependsOnExternalId` | `ScheduleDependentAsync` | The parent manifest's external id |
| `sources`, `map`, `dependsOn`, `configureEach` | `ScheduleManyDependentAsync` and the explicit batch forms | As for [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many), with `dependsOn` giving each source's parent external id |
| `options` | all | [ScheduleOptions](/docs/sdk-reference/scheduler-api/schedule#scheduleoptions): manifest settings, group settings, `.Dormant()` |
| `ct` | runtime methods | Cancels the call |

## Returns

The startup methods return the `SchedulerConfigurationBuilder`, for chaining.
`ScheduleDependentAsync` returns the created or updated `Manifest`, and
`ScheduleManyDependentAsync` every one of them.

## Behavior

- The inferred forms take `TInput` from `TTrain`'s `IServiceTrain<TInput, TOutput>` interface and
  leave the output unconstrained; the output of a scheduled run is discarded. The forms with
  explicit type parameters remain for compatibility.
- The builder keeps two pointers: the cursor (the last declared manifest) and the root (the last
  `Schedule`). `Schedule` sets both; `ThenInclude` and `Include` move the cursor and leave the root;
  `ScheduleMany` clears both.
- `ThenInclude` depends on the cursor and must follow `Schedule`, `Include` or another
  `ThenInclude`. `Include` depends on the root and must follow `Schedule`. Called without one,
  each throws `InvalidOperationException`.
- An `IncludeMany` item with no `DependsOn` depends on the root, so it needs a preceding
  `Schedule`; when every item names its parent, none is needed, which is how `IncludeMany` follows
  `ScheduleMany`. `ThenIncludeMany` throws `InvalidOperationException` when any item has no
  `DependsOn`.
- A dependent manifest has `ScheduleType.Dependent` and no interval or cron. Each polling cycle
  queues it when its parent's `LastSuccessfulRun` is later than the start of the dependent's
  latest run (successful or cancelled), both read from the database's clock. A parent success that
  lands while the dependent runs earns it another run, and several such successes collapse into
  one.
- A dependent's work queue entry gets `DependentPriorityBoost` (16 by default, see
  [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)) added to its priority, clamped
  to 0 to 31.
- Group dependencies must form a DAG. When the scheduler is built, the group-level edges of every
  `Schedule`, `ThenInclude`, `Include`, `ScheduleMany`, `ThenIncludeMany` and `IncludeMany` call
  are checked, and a cycle between groups throws `InvalidOperationException` naming the groups.
  Dependencies within one group are allowed. See
  [Dependent Trains: Cycle Detection](/docs/scheduler/dependent-trains#cycle-detection).

### Dormant option

`.Dormant()` on `ScheduleOptions` declares a dormant dependent. Its manifest has
`ScheduleType.DormantDependent`, appears in the topology, and is left out of both time-based and
dependent evaluation, so it never fires on its own: the parent train activates it through
`IDormantDependentContext`.

### IDormantDependentContext

The service a running parent uses to activate its dormant dependents is documented on
[IDormantDependentContext](/docs/sdk-reference/scheduler-api/i-dormant-dependent-context).

## Examples

A chain, extract then transform then load, and a branch from the root:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .Schedule<IExtractTrain>(
            "etl-extract", new ExtractInput(), Every.Minutes(5),
            options => options.Group("etl"))
        .Include<ITransformTrain>(
            "etl-transform", new TransformInput(),
            options => options.Group("etl").Priority(5))
            .ThenInclude<ILoadTrain>(
                "etl-load", new LoadInput(),
                options => options.Group("etl"))
        .Include<IValidateTrain>(
            "etl-validate", new TransformInput(),
            options => options.Group("etl").Dormant())));
```

`etl-transform` and `etl-validate` depend on `etl-extract`, `etl-load` on `etl-transform`; the
validator runs only when the extract train activates it.

A batch of dependents, each naming its parent:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .ScheduleMany<IExtractTrain>(
            "extract",
            Enumerable.Range(0, 10).Select(i => new ManifestItem($"{i}", new ExtractInput { Index = i })),
            Every.Minutes(5))
        .IncludeMany<ITransformTrain>(
            "transform",
            Enumerable.Range(0, 10).Select(i => new ManifestItem(
                $"{i}", new TransformInput { Index = i }, DependsOn: $"extract-{i}")))));
```

This declares `extract-0` to `extract-9` and `transform-0` to `transform-9`.

At runtime:

```csharp compile
public class PipelineSetup(ITraxScheduler scheduler)
{
    public async Task CreateAsync(CancellationToken ct)
    {
        await scheduler.ScheduleAsync<IExtractTrain, ExtractInput, Unit>(
            "fetch-data", new ExtractInput(), Cron.Hourly(), ct: ct);

        await scheduler.ScheduleDependentAsync<ITransformTrain, TransformInput, Unit>(
            externalId: "process-data",
            input: new TransformInput(),
            dependsOnExternalId: "fetch-data",
            ct: ct);
    }
}
```

## See also

- [Dependent Trains](/docs/scheduler/dependent-trains), how dependent runs are evaluated
- [IDormantDependentContext](/docs/sdk-reference/scheduler-api/i-dormant-dependent-context)
- [Schedule / ScheduleAsync](/docs/sdk-reference/scheduler-api/schedule) and [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many)
