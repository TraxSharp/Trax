---
layout: default
title: Scheduler API
parent: SDK Reference
nav_order: 3
has_children: true
---

# Scheduler API

The scheduler manages recurring and dependent trains through **manifests** — persistent records that define what train to run, when, and with what input. There are two contexts for scheduling:

1. **Startup configuration** — `AddScheduler(scheduler => ...)` inside `AddTrax`, where you declare schedules that are seeded when the application starts.
2. **Runtime API** — `ITraxScheduler` injected via DI, where you create/modify schedules dynamically at runtime.

Both share the same concepts: external IDs for upsert semantics, `Schedule` objects for timing, and `ManifestOptions` for per-job configuration.

## Quick Reference

### Setup & Configuration

| Method | Description |
|--------|-------------|
| [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | Adds the scheduler subsystem and configures global options (polling, retries, timeouts) |
| [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) | Customizes the built-in PostgreSQL local workers (enabled by default) |
| [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | Routes specific trains to a remote HTTP endpoint |
| [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | AWS Lambda base class for remote runners (`Trax.Runner.Lambda` package) |
### Scheduling Methods

| Method | Context | Description |
|--------|---------|-------------|
| [Schedule / ScheduleAsync](/docs/sdk-reference/scheduler-api/schedule) | Startup / Runtime | Schedules a single recurring train |
| [ScheduleMany / ScheduleManyAsync](/docs/sdk-reference/scheduler-api/schedule-many) | Startup / Runtime | Batch-schedules manifests from a collection with optional pruning |
| [Dependent Scheduling](/docs/sdk-reference/scheduler-api/dependent-scheduling) | Both | Schedules trains that run after a parent completes (`ThenInclude`, `ThenIncludeMany`, `Include`, `IncludeMany`, `ScheduleDependentAsync`, `ScheduleManyDependentAsync`) |

### Management

| Method | Description |
|--------|-------------|
| [Manifest Management](/docs/sdk-reference/scheduler-api/manifest-management) | `DisableAsync`, `EnableAsync`, `TriggerAsync` — runtime control of scheduled jobs |
| [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) | Enables automatic purging of old metadata for high-frequency trains |

### Helpers

| Page | Description |
|------|-------------|
| [Scheduling Helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | `Every`, `Cron`, `Schedule` record, and `ManifestOptions` — the building blocks for defining when and how jobs run |
