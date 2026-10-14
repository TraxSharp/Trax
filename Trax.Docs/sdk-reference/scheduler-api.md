---
title: Scheduler API
description: "Index of the Trax.Scheduler reference and its Lambda and SQS packages: AddScheduler, ITraxScheduler, scheduling methods, workers, runners and operations."
type: landing
area: scheduling
parent: SDK Reference
has_children: true
nav_order: 5
---

# Scheduler API

The scheduler runs trains from **manifests**: stored records of which train to run, when, and with
what input. Manifests are declared at startup on the builder `AddScheduler` passes its callback, or
created at runtime through `ITraxScheduler`. How the scheduler works is on
[Scheduler](/docs/scheduler).

## Trax.Scheduler

### Setup

| Page | What it documents |
|------|-------------------|
| [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | Registers the scheduler; the builder's global options and their ranges |
| [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers) | The built-in local worker pool |
| [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | Queued runs of chosen trains on a remote HTTP runner |
| [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | Synchronous runs on a remote HTTP runner |
| [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) | Periodic deletion of old metadata |
| [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | A runner process, with `UseTraxJobRunner` and `UseTraxRunEndpoint` |
| [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | A standalone worker that polls `background_job` |
| [AddTraxSchedulerLiveness](/docs/sdk-reference/scheduler-api/add-trax-scheduler-liveness) | The health check that fails when dispatch stops |

### Scheduling

| Page | What it documents |
|------|-------------------|
| [Schedule / ScheduleAsync](/docs/sdk-reference/scheduler-api/schedule) | One recurring manifest, and `ScheduleOptions` |
| [ScheduleMany / ScheduleManyAsync](/docs/sdk-reference/scheduler-api/schedule-many) | Many manifests of one train, with pruning |
| [Dependent scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling) | `ThenInclude`, `Include`, `IncludeMany`, `ThenIncludeMany`, `ScheduleDependentAsync`, `ScheduleManyDependentAsync` |
| [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | `Every`, `Cron`, the `Schedule` record and `ManifestOptions` |
| [IDormantDependentContext](/docs/sdk-reference/scheduler-api/i-dormant-dependent-context) | Activating dormant dependents from a running train |

### Runtime

| Page | What it documents |
|------|-------------------|
| [ITraxScheduler](/docs/sdk-reference/scheduler-api/i-trax-scheduler) | The runtime scheduling API, `TriggerGroupAsync` and the dead-letter methods |
| [Manifest management](/docs/sdk-reference/scheduler-api/manifest-management) | `DisableAsync`, `EnableAsync`, `TriggerAsync`, `ScheduleOnceAsync`, `CancelAsync`, `CancelGroupAsync` |
| [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service) | The operations the dashboard and the GraphQL API share |
| [IEffectSettingsService](/docs/sdk-reference/scheduler-api/i-effect-settings-service) | This process's effects and their settings |
| [ILogLevelService](/docs/sdk-reference/scheduler-api/i-log-level-service) | This process's log levels |
| [IDeadLetterRequeueJobs](/docs/sdk-reference/scheduler-api/i-dead-letter-requeue-jobs) | Requeue-all as a background job on this node |

## Trax.Scheduler.Sqs

| Page | What it documents |
|------|-------------------|
| [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | Queued runs of chosen trains through an SQS queue, and `SqsJobRunnerHandler` |

## Trax.Scheduler.Lambda

| Page | What it documents |
|------|-------------------|
| [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) | Queued runs of chosen trains on an AWS Lambda function |
| [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) | Synchronous runs on an AWS Lambda function |

## Trax.Runner.Lambda

| Page | What it documents |
|------|-------------------|
| [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | The base class of a Lambda function that runs trains |

## Trax.Effect.Data

| Page | What it documents |
|------|-------------------|
| [IWorkQueuePromotion](/docs/sdk-reference/scheduler-api/i-work-queue-promotion) | Confirming and resolving staged work queue entries |
