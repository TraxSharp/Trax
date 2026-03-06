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
| [AddScheduler]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-scheduler.md %}) | Adds the scheduler subsystem and configures global options (polling, retries, timeouts) |
| [UseLocalWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-local-workers.md %}) | Built-in PostgreSQL local workers (recommended) |
| [UseRemoteWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-workers.md %}) | Dispatches jobs via HTTP POST to a remote endpoint |
| [UseHangfire]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-hangfire.md %}) | Configures Hangfire as the execution backend (deprecated) |

### Scheduling Methods

| Method | Context | Description |
|--------|---------|-------------|
| [Schedule / ScheduleAsync]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule.md %}) | Startup / Runtime | Schedules a single recurring train |
| [ScheduleMany / ScheduleManyAsync]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule-many.md %}) | Startup / Runtime | Batch-schedules manifests from a collection with optional pruning |
| [Dependent Scheduling]({{ site.baseurl }}{% link sdk-reference/scheduler-api/dependent-scheduling.md %}) | Both | Schedules trains that run after a parent completes (`ThenInclude`, `ThenIncludeMany`, `Include`, `IncludeMany`, `ScheduleDependentAsync`, `ScheduleManyDependentAsync`) |

### Management

| Method | Description |
|--------|-------------|
| [Manifest Management]({{ site.baseurl }}{% link sdk-reference/scheduler-api/manifest-management.md %}) | `DisableAsync`, `EnableAsync`, `TriggerAsync` — runtime control of scheduled jobs |
| [AddMetadataCleanup]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-metadata-cleanup.md %}) | Enables automatic purging of old metadata for high-frequency trains |

### Helpers

| Page | Description |
|------|-------------|
| [Scheduling Helpers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/scheduling-helpers.md %}) | `Every`, `Cron`, `Schedule` record, and `ManifestOptions` — the building blocks for defining when and how jobs run |
