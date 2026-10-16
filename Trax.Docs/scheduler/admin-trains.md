---
title: Administrative Trains
description: "The internal trains and hosted services AddScheduler registers, which provider each needs, how they meet at the work queue and why they skip MaxActiveJobs."
type: explanation
area: scheduling
nav_order: 3
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.Build.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/AdminTrains.cs
---

# Administrative Trains

The scheduler runs its own work as trains, the same way it runs yours, so every cycle has a
metadata row and shows up in the dashboard when it fails. `AddScheduler()` registers these trains
and the hosted services that run them; you never register or start them yourself, and you pass
only your own assemblies to `AddMediator()`.

| Train | What it does |
|---|---|
| [ManifestManager](/docs/scheduler/admin-trains/manifest-manager) | Cancels timed-out runs, fails stale ones, dead-letters manifests out of retries, and writes due manifests to the work queue |
| [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher) | Reads the work queue, applies the capacity limits, creates each run's metadata and hands it to a job submitter |
| [JobRunner](/docs/scheduler/admin-trains/job-runner) | Runs your train on a worker and records its success on the manifest |
| [MetadataCleanup](/docs/scheduler/admin-trains/metadata-cleanup) | Deletes old metadata rows; on by default with a database provider, tuned by `AddMetadataCleanup()` |
| `DeadLetterCleanupTrain` | Deletes resolved dead letters past their retention; see [Dead letter auto-purge](/docs/scheduler/dead-letters-and-cleanup#dead-letter-auto-purge) |

`AdminTrains.Types` lists them, interfaces and classes, including the InMemory variant of the
ManifestManager. Their runs do not count toward `MaxActiveJobs`, they are never timed out, and the
dashboard leaves them out of its statistics by default.

## The hosted services

Which services run depends on the data provider. Postgres and SQLite count as a database provider;
InMemory does not.

| Service | Registered | What it does |
|---|---|---|
| `SchedulerConfigBootstrapHostedService` | always | Applies the scheduler settings saved from the dashboard or `updateScheduler` over the values in code, then rereads them every 5 seconds so a save on any host reaches this one |
| `SchedulerStartupService` | always | Runs once, before the polling services. With a database provider it first fails every `InProgress` run that started before this host did (`RecoverStuckJobsOnStartup`, on by default), then seeds the manifests the builder declared, prunes [orphaned manifests](/docs/scheduler/orphan-manifest-cleanup) and deletes manifest groups with no manifests left. InMemory only seeds |
| `ManifestManagerPollingService` | always | Runs the ManifestManager every `ManifestManagerPollingInterval` (5 seconds by default). On InMemory it runs `InMemoryManifestManagerTrain`, which dispatches due manifests inline instead of writing to the work queue |
| `JobDispatcherPollingService` | database provider | Runs the JobDispatcher every `JobDispatcherPollingInterval` (2 seconds by default), and sooner when woken |
| `QueuedWorkListenerService` | database provider | Wakes the dispatcher when work is queued, so a queued run does not wait for the next poll |
| `LocalWorkerService` | database provider, with the default submitter | The worker pool that runs jobs from `trax.background_job`; see [How jobs reach workers](/docs/scheduler/job-submission) |
| `MetadataCleanupPollingService` | database provider | Runs the MetadataCleanup train every `CleanupInterval`, and once at startup |
| `DeadLetterCleanupPollingService` | database provider | Runs the dead-letter purge every `DeadLetterCleanupInterval` (1 hour by default). Always registered, because `AutoPurgeDeadLetters` can be turned on at runtime |

The intervals and switches are options of [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler).

Startup seeding is strict: a manifest that cannot be seeded stops the host, because broken
manifest configuration should not start quietly. A seed that fails on what the provider's SQL
dialect calls transient (a lost or refused connection, a timeout, a deadlock or serialization
failure, SQLite's busy and locked codes) is tried up to five times in all, the delay doubling from
2 seconds. Any other failure, a constraint violation included, fails at once. Pruning is the
exception: it is housekeeping, so a failed prune is logged and the host starts anyway.

The stuck-job recovery has no way to know which process owns a run, so "started before this host"
is the whole test. On a single host that is exactly the runs a crash left behind. Where several
hosts or remote workers share the database, it also fails a run still executing elsewhere, which
is why it can be turned off with `RecoverStuckJobsOnStartup(false)`.

## The work queue

Every run the scheduler starts goes through the `work_queue` table. The ManifestManager does not
hand a due manifest to a worker; it writes a work queue entry. `TriggerAsync`, a dead-letter
requeue, a dashboard re-run and a GraphQL `queue` mutation write entries too. The JobDispatcher is
the only thing that reads them.

```
ManifestManager (schedules) ──┐
TriggerAsync, requeues ───────┼──► work_queue ──► JobDispatcher ──► job submitter ──► JobRunner
Dashboard, queue mutations ───┘
```

That gives capacity one place to be enforced. `MaxActiveJobs`, per-group limits, priority and
[subject serialization](/docs/mediator/queued-work#two-mutations-for-one-record)
are all applied by the dispatcher, whatever wrote the entry. The ManifestManager can decide what is
due without knowing how busy the system is, and the two run on their own intervals, so each can be
tuned on its own.

An entry records the train to run (its interface's full name), the serialized input and its type,
its status (`Queued`, then `Dispatched`), the manifest it came from if any, and, once dispatched,
the metadata row of its run.

On Postgres a trigger on `work_queue` sends a notification in the transaction that queues an
entry, so every dispatcher, on any host, wakes when that transaction commits and hears nothing if
it rolls back. A run queued on an API host starts within moments even when the scheduler polls
every 30 seconds. On SQLite the wake comes from a save in the same process. The poll stays the
fallback, and the only clock for an entry whose `ScheduledAt` comes due, a dispatch retried after a
backoff, or a manifest that is enabled again. A connection pooler in transaction mode, such as
PgBouncer, does not carry `LISTEN`, so behind one the dispatcher polls. The listener holds one
unpooled connection, shown in `pg_stat_activity` as `trax_queued_work_listener`; when it is lost
the listener logs a warning and tries again after 1 second, doubling to 30.

## InMemory

With InMemory there is no work queue reader. `InMemoryManifestManagerTrain` runs only
`LoadManifestsJunction`, `ReapFailedJobsJunction`, `DetermineJobsToQueueJunction` and
`InMemoryDispatchJobsJunction`, which runs each due manifest through the in-memory job submitter
inline. It has no timeout cancellation, no stale `Pending` or `InProgress` recovery and no stale
staged-entry sweep, and a run queued through GraphQL (`mode: QUEUE`) stays queued. It resolves a
manifest's stored input type only among the registered trains' inputs, as the dispatcher does; a
manifest whose input resolves to nothing is logged and leaves no `Pending` row.

## Several hosts

Each service has its own way of staying safe when several hosts share the database: the
ManifestManager takes an advisory lock so one host runs it per cycle, the JobDispatcher claims
entries with `FOR UPDATE SKIP LOCKED`, and the cleanups are idempotent. [Multi-Server
Concurrency](/docs/scheduler/concurrency) explains each.

## Excluding your own trains from MaxActiveJobs

A high-frequency internal train of your own can be left out of the count the same way the
administrative trains are:

```csharp compile
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.MaxActiveJobs(100).ExcludeFromMaxActiveJobs<ISyncTrain>()
        )
);
```

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup)
