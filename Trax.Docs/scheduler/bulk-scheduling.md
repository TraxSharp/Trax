---
title: Schedule manifests in bulk
description: "How to schedule many manifests of one train at startup or at runtime, vary settings per item, group them under shared dispatch limits and prune the stale ones."
type: how-to
area: scheduling
nav_order: 6
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.BatchScheduling.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/TraxScheduler/TraxScheduler.cs
---

# Schedule manifests in bulk

One train often runs as many manifests: a sync per table, an import per partition. The parameters
of each call are on [ScheduleMany / ScheduleManyAsync](/docs/sdk-reference/scheduler-api/schedule-many).

## At startup, from a known list

Use the builder's name-based `ScheduleMany` with one `ManifestItem` per manifest. The name gives
each manifest's external id (`"{name}-{item id}"`), the group they share and their prune prefix
(`"{name}-"`):

```csharp compile
string[] tables = ["users", "orders", "products"];

services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.ScheduleMany<ISyncTableTrain>(
                "table-sync",
                tables.Select(table => new ManifestItem(
                    table,
                    new SyncTableInput { TableName = table }
                )),
                Every.Minutes(5)
            )
        )
);
```

This seeds `table-sync-users`, `table-sync-orders` and `table-sync-products` in group
`table-sync` when the host starts, with the same upsert semantics as `Schedule`.

## At runtime, from data

When the set comes from a database, a config file or an API, call `ScheduleManyAsync`. It writes
the whole batch in one transaction, so if any manifest fails to save, none is saved:

```csharp compile
var tables = new[]
{
    (Name: "customer", SliceCount: 100),
    (Name: "partner", SliceCount: 10),
};

// Flatten tables x slices into one list
var slices = tables.SelectMany(t =>
    Enumerable.Range(0, t.SliceCount).Select(slice => (Table: t.Name, Slice: slice))
);

await scheduler.ScheduleManyAsync<ISyncTableTrain, SyncTableInput, Unit, (string Table, int Slice)>(
    slices,
    item => (
        $"sync-{item.Table}-{item.Slice}",
        new SyncTableInput { TableName = item.Table, SliceIndex = item.Slice }
    ),
    Every.Minutes(5),
    options => options.Group("sync").PrunePrefix("sync-")
);
```

## Vary settings per item

Settings for the whole batch go in `options`. Settings that differ per item go in `configureEach`,
which receives each source item and its `ManifestOptions` after `options` has been applied:

```csharp compile
string[] tables = ["users", "orders", "products", "logs"];

await scheduler.ScheduleManyAsync<ISyncTableTrain, SyncTableInput, Unit, string>(
    tables,
    table => ($"sync-{table}", new SyncTableInput { TableName = table }),
    Every.Minutes(5),
    options => options.Group("sync"),
    configureEach: (table, manifest) =>
    {
        manifest.MaxRetries = table == "orders" ? 5 : 3;
        manifest.Timeout = table == "logs" ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(10);
    }
);
```

`configureEach` is on `ScheduleManyAsync` and on the builder's typed
`ScheduleMany<TTrain, TInput, TOutput, TSource>`, not on the `ManifestItem` form. The schedule is
shared by the whole batch: manifests that need different intervals are separate batches, or
separate `Schedule` calls.

## Group them under shared limits

A batch's manifests share a group, and the group's dispatch settings apply to all of them, from
code or from the dashboard's Manifest Group page:

```csharp compile
string[] tables = ["users", "orders"];

services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.ScheduleMany<ISyncTableTrain>(
                "table-sync",
                tables.Select(table => new ManifestItem(
                    table,
                    new SyncTableInput { TableName = table }
                )),
                Every.Minutes(5),
                options => options.Group(group => group.MaxActiveJobs(2).Priority(10))
            )
        )
);
```

A single `Schedule` joins a group by naming it, `options => options.Group("table-sync")`. What
each setting does, and what a restart writes back over a change made in the dashboard, is in
[How the scheduler decides when a manifest runs](/docs/scheduler/scheduling-options#per-group-dispatch-controls).

## Prune the manifests that are gone

When the source shrinks between deployments, the manifests for the removed items stay in the
database and keep running. A prune prefix deletes them: after the batch is written, every manifest
whose external id starts with the prefix and was not in the batch is deleted. The name-based
`ScheduleMany` prunes with `"{name}-"`, inside its own group only, so `ScheduleMany("sync", ...)`
never deletes the manifests of `ScheduleMany("sync-users", ...)`. Elsewhere, set one with
`options.PrunePrefix("...")`.

- An explicit prefix has no group scope, so `AddScheduler` refuses to start when one batch's
  prefix covers another batch's, or a single `Schedule`'s external id (see
  [Startup errors](/docs/scheduler/startup-errors)).
- A manifest with a run `Pending` or `InProgress` is not pruned; it goes at a later prune, once the
  run has finished.
- The prune runs after the batch's transaction commits, on its own context. A failed prune does
  not roll back the batch; it is logged as a warning and runs again next time.

Removing a whole `Schedule` call is caught by a different mechanism, at startup; see
[Orphan Manifest Cleanup](/docs/scheduler/orphan-manifest-cleanup).

## SDK Reference

> [ScheduleMany / ScheduleManyAsync](/docs/sdk-reference/scheduler-api/schedule-many) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Every / Cron](/docs/sdk-reference/scheduler-api/scheduling-helpers)
