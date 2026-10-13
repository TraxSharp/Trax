---
title: Scheduler namespaces
description: "The namespace and package of each public type a scheduler host uses: manifests, schedules, options, ITraxScheduler and the dormant dependent context."
type: reference
area: scheduling
nav_order: 21
source:
  - Trax.Scheduler/src/Trax.Scheduler
---

# Scheduler namespaces

The scheduler spans several packages. These are the public types a scheduler host names most
often.

| Type | Namespace | Package |
|------|-----------|---------|
| `IManifestProperties` | `Trax.Effect.Models.Manifest` | `Trax.Effect` |
| `Exclude`, `Exclusion` | `Trax.Effect.Models.Manifest` | `Trax.Effect` |
| `MisfirePolicy` | `Trax.Effect.Enums` | `Trax.Effect` |
| `AddScheduler`, `AddTraxWorker`, `AddTraxJobRunner`, `AddTraxSchedulerLiveness` | `Trax.Scheduler.Extensions` | `Trax.Scheduler` |
| `Cron`, `Every`, `Schedule` | `Trax.Scheduler.Services.Scheduling` | `Trax.Scheduler` |
| `ITraxScheduler` | `Trax.Scheduler.Services.TraxScheduler` | `Trax.Scheduler` |
| `ScheduleOptions`, `ManifestOptions`, `ManifestGroupOptions`, `ManifestItem`, `LocalWorkerOptions` | `Trax.Scheduler.Configuration` | `Trax.Scheduler` |
| `IJobSubmitter`, `PostgresJobSubmitter` | `Trax.Scheduler.Services.JobSubmitter` | `Trax.Scheduler` |
| `IDormantDependentContext` | `Trax.Scheduler.Services.DormantDependentContext` | `Trax.Scheduler` |
| `JobRunnerTrain` | `Trax.Scheduler.Trains.JobRunner` | `Trax.Scheduler` |
| `UseSqsWorkers`, `SqsJobRunnerHandler` | `Trax.Scheduler.Sqs.Extensions`, `Trax.Scheduler.Sqs.Lambda` | `Trax.Scheduler.Sqs` |
| `UseLambdaWorkers`, `UseLambdaRun` | `Trax.Scheduler.Lambda.Extensions` | `Trax.Scheduler.Lambda` |
| `TraxLambdaFunction` | `Trax.Runner.Lambda` | `Trax.Runner.Lambda` |

The data providers (`UsePostgres`, `UseSqlite`, `UseInMemory`) are in the `Extensions` namespace of
their own packages, such as `Trax.Effect.Data.Postgres.Extensions`. [Packages](/docs/reference/packages)
lists every package.
