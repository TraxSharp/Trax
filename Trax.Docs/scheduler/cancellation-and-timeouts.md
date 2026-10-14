---
title: Cancellation and timeouts in the scheduler
description: "How the scheduler stops runs: the worker's shutdown grace period, CancelAsync and the dashboard's cancel, job timeouts, and what each records."
type: explanation
area: scheduling
nav_order: 11
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/LocalWorkerService/LocalWorkerService.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/CancellationRegistry
  - Trax.Scheduler/src/Trax.Scheduler/Trains/ManifestManager/Junctions/CancelTimedOutJobsJunction.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/JobSubmitter/IJobSubmitter.cs
---

# Cancellation and timeouts in the scheduler

A run the scheduler dispatches can be stopped three ways: the host shuts down, someone cancels it,
or it runs past its timeout. All three end in the same place, an `OperationCanceledException` the
train sees through its token or before its next `EffectJunction`, and a `ServiceTrain` records it as
`Cancelled`. How a train handles the token itself is in
[How cancellation works](/docs/cross-cutting/cancellation-tokens); this page is what the scheduler
adds on top.

## Shutdown and the grace period

The scheduler's polling services (the ManifestManager, the JobDispatcher, metadata cleanup) pass
the host's `stoppingToken` to the trains they run, so a shutdown cancels them straight away.

The local worker that runs your jobs does not hand its jobs the stopping token. Each run gets its
own `CancellationTokenSource`, and when the host signals shutdown the worker calls
`CancelAfter(ShutdownTimeout)` on it, 30 seconds by default:

```
Host signals shutdown (stoppingToken fires)
        │
        ▼
┌───────────────────────────────────────────┐
│  runCts.CancelAfter(ShutdownTimeout)      │  ← grace period starts
│                                           │
│  The run carries on, and has until the    │
│  grace period ends to finish              │
│                                           │
│  Grace period over: runCts fires          │  ← the run is cancelled
└───────────────────────────────────────────┘
```

The grace period exists for runs in the middle of something that should not be cut off, a
transaction or a call to an external system, and is long enough for most of them to finish
normally. A run still going at the end is cancelled like any other.

When a run finishes, inside the grace period or after being cancelled, the worker deletes its
`background_job` row without the stopping token. That token has already fired, and the delete is
bookkeeping for finished work. Were it cancellable, every job that finished during shutdown would
leave its row behind for another worker to claim after `VisibilityTimeout`, only to refuse it as no
longer `Pending`.

With a `BatchSize` above 1, a job the worker claimed but had not started when shutdown began is
not started at all. Its `fetched_at` is cleared so another worker can claim it at once, rather than
each remaining job getting a grace period of its own.

`ShutdownTimeout` and `BatchSize` are set with
[ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers):

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .ConfigureLocalWorkers(options => options.ShutdownTimeout = TimeSpan.FromSeconds(60))
    )
);
```

## Cancelling a run

The dashboard's cancel button, `ITraxScheduler.CancelAsync(externalId)` for every pending and
running execution of a manifest, and `CancelGroupAsync(groupId)` for a manifest group, all use the
same two layers:

1. **The database flag.** `CancellationRequested` is set on each run's metadata row. This works on
   any host. A run still `Pending` is recorded `Cancelled` without running when the job runner picks
   it up. A running one sees the flag through `CancellationCheckProvider`, registered by
   `AddJunctionProgress()`, before its next `EffectJunction`.
2. **The registry.** On the host that is running the job, the worker has registered the run's
   `CancellationTokenSource` with `ICancellationRegistry` under its metadata id, and the cancel
   calls `TryCancel`, which cancels the token at once. The first registration for a run holds it
   until that worker removes it with `Unregister(metadataId, cts)`, so a second delivery of the same
   job on the same host, which does not run the train, cannot take the registration from the one
   that does.

```
Dashboard cancel, CancelAsync, CancelGroupAsync
    ├──→ SET cancel_requested = true      (always, any host)
    └──→ ICancellationRegistry.TryCancel() (the host running the job)
            ├─ found     → token cancelled → the awaited call throws
            └─ not found → nothing; the flag covers it
```

The registry is the only layer that stops a train built from plain `Junction`s: the flag is read by
a junction effect, and junction effects run only around an `EffectJunction`. A train of plain
junctions running on another host runs to the end. See
[Make a junction cancellable](/docs/cross-cutting/make-a-junction-cancellable#4-let-a-run-stop-from-another-host).

A cancelled run is not retried and creates no dead letter. A cancelled run of a scheduled manifest
uses up the occurrence it ran for: the manifest next runs at its next scheduled time, not on the
next polling cycle.

## Timeouts

Every polling cycle, the ManifestManager's `CancelTimedOutJobsJunction` looks at each run in
progress and cancels any that has run longer than its timeout, through the same two layers. A run's
timeout is the `TimeoutSeconds` of the manifest of the run at the root of its `ParentId` chain, so a
nested train shares the timeout of the scheduled run it belongs to, or `DefaultJobTimeout`
(20 minutes) when that manifest sets none. `DefaultJobTimeout` applies only to runs a scheduler
dispatched; a train run directly on the train bus is not timed out. Runs of a manifest disabled
while they run are still timed out. The resolution rules are in
[Timeout Enforcement](/docs/scheduler/scheduling-options#timeout-enforcement).

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .DefaultJobTimeout(TimeSpan.FromMinutes(30))
        .Schedule<ISyncTrain>(
            "sync-hourly",
            new SyncInput(),
            Every.Hours(1),
            options => options.Timeout(TimeSpan.FromMinutes(10)))
    )
);
```

A timeout cancels the run rather than failing it. The run is recorded `Cancelled` and is not
retried, so an hourly job that always runs past its timeout runs once an hour, not continuously.
Because the cancel uses the same layers, a timed-out run on another host stops only at an
`EffectJunction`, and one made of plain junctions is not stopped by its timeout at all. The
stale-run reaper, which fails a run whose worker died, is a separate safety net; see
[ManifestManager](/docs/scheduler/admin-trains/manifest-manager).

## Job submitters

`IJobSubmitter` has `EnqueueAsync` overloads that take a `CancellationToken`, with default
implementations that call the overloads without one, so a custom submitter written before them
still compiles. The built-in submitters implement them: the Postgres submitter passes the token to
`SaveChangesAsync`, and the in-memory one passes it to the train's `Run`. See
[Job Submission](/docs/scheduler/job-submission).

## SDK Reference

> [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers) | [CancelAsync / CancelGroupAsync](/docs/sdk-reference/scheduler-api/manifest-management) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress)
