---
title: How jobs reach workers
description: "How the JobDispatcher hands a run to a job submitter, which submitter a host gets, how the built-in local workers claim and recover jobs, and custom submitters."
type: explanation
area: scheduling
nav_order: 4
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/JobSubmitter
  - Trax.Scheduler/src/Trax.Scheduler/Services/LocalWorkerService/LocalWorkerService.cs
---

# How jobs reach workers

The JobDispatcher decides that a queued entry runs now. It does not run it. It creates the run's
metadata row, commits, and hands the run's id and input to a job submitter (`IJobSubmitter`). The
submitter's only job is to get that run to something that executes the
[JobRunner](/docs/scheduler/admin-trains/job-runner), which loads the row, runs your train and
records the result.

Keeping that step behind one interface is what lets the same scheduler run its jobs on threads in
its own process, on a separate worker fleet, or in AWS Lambda, without anything above the
submitter changing.

## Which submitter a host gets

| The host has | Submitter | Where the job runs |
|---|---|---|
| `UsePostgres()` or `UseSqlite()` | `PostgresJobSubmitter`, which inserts a row into `trax.background_job` | On `LocalWorkerService` threads in this process, started automatically |
| `UseInMemory()` | The in-memory submitter | Inline, inside the call that submits it. Meant for tests and prototypes |
| `OverrideSubmitter(...)` | Yours | Wherever it sends it. Replaces both defaults, and no local workers are started |
| `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` with routing | An HTTP, SQS or Lambda submitter, for the trains routed to it | On a runner elsewhere; every other train keeps the default. See [Choosing where trains run](/docs/scheduler/remote-execution) |

Only `PostgresJobSubmitter` is a public type. The others are selected by their builder methods.

## Why the metadata row is committed first

The dispatcher commits the run's metadata row and the entry's move to `Dispatched` in one
transaction, and calls the submitter only after it commits. The worker that picks the job up has to
read that row, and the in-memory submitter runs the train inside the call itself, so the row must
already be visible when the call is made.

The cost is a window where the row exists and the job may not: the submit can fail after the
commit. The dispatcher handles that case, and the rule it follows is the subject of
[Dispatch failures](/docs/scheduler/admin-trains/job-dispatcher#dispatch-failures).

## The built-in local workers

With Postgres (or SQLite) the default submitter writes one row per job into `trax.background_job`,
in the same database the rest of Trax uses, and `LocalWorkerService` runs a pool of worker tasks
that take jobs from it. There is no other queue to run or connect to.

```
JobDispatcher ──► PostgresJobSubmitter ──► trax.background_job ──► LocalWorkerService (N workers)
                  INSERT (metadata_id,                                each: claim ► run ► delete
                  input, priority)
```

The pool's size, polling interval, batch size, visibility timeout and shutdown grace are
`LocalWorkerOptions`, set with [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers).

### Worker Lifecycle

Each worker loops through three phases.

**Claim**, in a transaction:

```sql
SELECT * FROM trax.background_job
WHERE fetched_at IS NULL
   OR fetched_at < NOW() - make_interval(secs => :visibility_timeout)
ORDER BY priority DESC, created_at ASC
LIMIT :batch_size
FOR UPDATE SKIP LOCKED
```

`FOR UPDATE SKIP LOCKED` gives each job to exactly one worker without workers waiting on each
other: a row another worker has locked is skipped, not waited for. Higher priorities go first, and
jobs of the same priority in the order they were queued. The worker sets `fetched_at` on what it
claimed and commits.

**Run**, in a fresh DI scope: the worker resolves the JobRunner and runs it with the run's id and
input. While a job runs, the worker refreshes `fetched_at` on it, and on the jobs waiting behind it
in its batch, every third of the visibility timeout, so a job that runs longer than the timeout
keeps its claim. Cancelling a run from the dashboard or `ITraxScheduler` reaches the running job
however long it has been running.

**Delete**, whether the job succeeded or failed: the worker deletes the `background_job` row. The
delete does not take the host's stopping token, so a job that finishes inside the
[shutdown grace period](/docs/scheduler/cancellation-and-timeouts#shutdown-and-the-grace-period) is still removed rather than left to be claimed again. The table is only a transient
queue; the run's history is in its metadata row.

When the host starts shutting down in the middle of a batch, the jobs not yet started are released
(their `fetched_at` is cleared), so any worker can claim them at once.

### Crash recovery

A worker that dies after claiming a job stops refreshing `fetched_at`. Once the timestamp is older
than the visibility timeout, the claim query matches the job again and another worker runs it:

```
Worker A claims job #1 → fetched_at = 10:00:00
Worker A crashes at 10:01:00
At 10:30:00 (with the default 30-minute timeout):
Worker B's claim query finds job #1 (fetched_at < NOW() - 30m)
Worker B claims and runs job #1
```

The second delivery does not run the train twice. The run's metadata row decides which delivery
runs it, as [JobRunner: Duplicate deliveries](/docs/scheduler/admin-trains/job-runner#duplicate-deliveries)
describes.

## InMemory

With `UseInMemory()` the submitter runs the train inside the submit call, synchronously. There are
no worker threads and no tables, so tests get the scheduling API without a database. There is no
work queue reader either: the InMemory ManifestManager dispatches due manifests itself, inline.
Retry delays, decision replay and the stale-run reapers do not run there, which is why retries and
dead letters are best tested against a real Postgres (see
[Administrative Trains: InMemory](/docs/scheduler/admin-trains#inmemory)).

## Elsewhere

The same interface takes runs off the scheduler's process. Three of the models push each run to a
runner: over HTTP, through an SQS queue, or by invoking a Lambda function directly. The fourth runs
a standalone worker process that takes jobs from the same `background_job` table the local
workers use. Every model shares the one database. [Choosing where trains
run](/docs/scheduler/remote-execution) compares them.

## A custom job submitter

A host that needs another transport implements `IJobSubmitter` and registers it with
`OverrideSubmitter`. Two overloads are required; the priority and cancellation overloads have
default implementations that fall back to them, so a submitter that ignores priority need not
implement them:

```csharp compile=custom
using Trax.Scheduler.Services.JobSubmitter;

public class ExternalQueueJobSubmitter(IExternalJobQueue queue) : IJobSubmitter
{
    public Task<string> EnqueueAsync(long metadataId) =>
        queue.SendAsync(metadataId, input: null, priority: 0, CancellationToken.None);

    public Task<string> EnqueueAsync(long metadataId, object input) =>
        queue.SendAsync(metadataId, input, priority: 0, CancellationToken.None);

    // Optional: carry the entry's priority to the backend.
    public Task<string> EnqueueAsync(
        long metadataId,
        object input,
        int priority,
        CancellationToken cancellationToken
    ) => queue.SendAsync(metadataId, input, priority, cancellationToken);
}
```

```csharp compile=custom
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;

services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(assembly)
        .AddScheduler(scheduler =>
            scheduler.OverrideSubmitter(s =>
                s.AddScoped<IJobSubmitter, ExternalQueueJobSubmitter>()
            )
        )
);
```

Whatever consumes the queue runs the JobRunner the way the local workers do: it resolves
`IJobRunnerTrain` and runs it with `new RunJobRequest(metadataId, input)`, in a host where
`AddTraxJobRunner` has registered it.

Moving a host off the removed Hangfire backend is covered in
[Migrate from Hangfire](/docs/migration-guides/migrate-from-hangfire).

## SDK Reference

> [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers) | [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [OverrideSubmitter](/docs/sdk-reference/scheduler-api/add-scheduler)
