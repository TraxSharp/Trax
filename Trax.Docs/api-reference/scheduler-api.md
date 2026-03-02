---
layout: default
title: Scheduler API
parent: API Reference
nav_order: 3
has_children: true
---

# Scheduler API

The scheduler manages recurring and dependent trains through **manifests** — persistent records that define what train to run, when, and with what input. There are two contexts for scheduling:

1. **Startup configuration** — `AddScheduler(scheduler => ...)` inside `AddTrax.CoreEffects`, where you declare schedules that are seeded when the application starts.
2. **Runtime API** — `IManifestScheduler` injected via DI, where you create/modify schedules dynamically at runtime.

Both share the same concepts: external IDs for upsert semantics, `Schedule` objects for timing, and `ManifestOptions` for per-job configuration.

## Quick Reference

### Setup & Configuration

| Method | Description |
|--------|-------------|
| [AddScheduler]({{ site.baseurl }}{% link api-reference/scheduler-api/add-scheduler.md %}) | Adds the scheduler subsystem and configures global options (polling, retries, timeouts) |
| [UsePostgresTaskServer]({{ site.baseurl }}{% link api-reference/scheduler-api/use-postgres-task-server.md %}) | Built-in PostgreSQL task server (recommended) |
| [UseHangfire]({{ site.baseurl }}{% link api-reference/scheduler-api/use-hangfire.md %}) | Configures Hangfire as the task server backend (deprecated) |

### Scheduling Methods

| Method | Context | Description |
|--------|---------|-------------|
| [Schedule / ScheduleAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %}) | Startup / Runtime | Schedules a single recurring train |
| [ScheduleMany / ScheduleManyAsync]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule-many.md %}) | Startup / Runtime | Batch-schedules manifests from a collection with optional pruning |
| [Dependent Scheduling]({{ site.baseurl }}{% link api-reference/scheduler-api/dependent-scheduling.md %}) | Both | Schedules trains that run after a parent completes (`ThenInclude`, `ThenIncludeMany`, `Include`, `IncludeMany`, `ScheduleDependentAsync`, `ScheduleManyDependentAsync`) |

### Management

| Method | Description |
|--------|-------------|
| [Manifest Management]({{ site.baseurl }}{% link api-reference/scheduler-api/manifest-management.md %}) | `DisableAsync`, `EnableAsync`, `TriggerAsync` — runtime control of scheduled jobs |
| [AddMetadataCleanup]({{ site.baseurl }}{% link api-reference/scheduler-api/add-metadata-cleanup.md %}) | Enables automatic purging of old metadata for high-frequency trains |

### Helpers

| Page | Description |
|------|-------------|
| [Scheduling Helpers]({{ site.baseurl }}{% link api-reference/scheduler-api/scheduling-helpers.md %}) | `Every`, `Cron`, `Schedule` record, and `ManifestOptions` — the building blocks for defining when and how jobs run |
