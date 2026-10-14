---
title: Migrate from Hangfire
description: "Moving a scheduler host off the Hangfire backend (UseHangfire and Trax.Scheduler.Hangfire) onto the built-in Postgres local workers."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 7
---

# Migrate from Hangfire

Applies from: Trax.Scheduler 1.0.0. No Trax.Scheduler release contains `UseHangfire` or the
`Trax.Scheduler.Hangfire` package: they belong to the scheduler's earlier builds, before its first
release under this name.

The scheduler used to hand each job to Hangfire's `IBackgroundJobClient.Enqueue()`. It now runs
jobs on its own local workers, which claim rows from one `trax.background_job` table through the
same data context as the rest of Trax. With Postgres configured they are on by default. How they
work is in [Job submission](/docs/scheduler/job-submission).

## What changes

Hangfire brought three NuGet packages and its own schema of ten or more tables, and Trax used one
call of it with retries, recurring jobs and continuations turned off. Retries, schedules and
dead letters were always Trax's. The local workers replace the one call, and the Hangfire dashboard
is replaced by the [Trax dashboard](/docs/dashboard).

## 1. Remove UseHangfire

```diff
  builder.Services.AddTrax(trax => trax
      .AddEffects(effects => effects
          .UsePostgres(connectionString)
      )
      .AddScheduler(scheduler => scheduler
-         .UseHangfire(connectionString)
          .Schedule<IMyTrain>("my-job", new MyInput(), Every.Minutes(5))
      )
  );

- app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [] });
```

Local workers need no call. To change their count, polling interval or timeouts, use
[`ConfigureLocalWorkers`](/docs/sdk-reference/scheduler-api/configure-local-workers).

## 2. Remove the package

```diff
  <ItemGroup>
      <PackageReference Include="Trax.Scheduler" />
-     <PackageReference Include="Trax.Scheduler.Hangfire" />
  </ItemGroup>
```

## 3. Remove the usings

```diff
- using Hangfire;
- using Trax.Scheduler.Hangfire.Extensions;
```

## 4. Start the host

The startup migrations create `trax.background_job` if it is not there yet. Nothing has to be
run by hand.

## 5. Drop the Hangfire schema

Once jobs run on the local workers, the `hangfire` schema is unused:

```sql
DROP SCHEMA IF EXISTS hangfire CASCADE;
```

## Related

- [Job submission](/docs/scheduler/job-submission): local workers, in-memory workers and remote execution.
- [Comparison](/docs/reference/comparison): Trax next to Hangfire and Quartz.NET.
