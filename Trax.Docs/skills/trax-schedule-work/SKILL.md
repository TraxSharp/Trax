---
name: trax-schedule-work
description: Use when a Trax app runs trains on a schedule, in a queue or on workers, or changes how it does. Covers AddScheduler, manifests (Schedule, Every, Cron, ScheduleOnce, ThenInclude, Include), ITraxScheduler and TriggerAsync, the work queue and mode QUEUE, retries and dead letters, scheduler option value ranges such as polling intervals and worker counts, which database providers support what (Postgres, SQLite, InMemory), local workers, AddTraxWorker, remote runners, Lambda and SQS, deployment topologies, the leader lock and the liveness health check.
metadata:
  internal: true
---

# Schedule work with Trax

`Trax.Scheduler` runs trains without a caller waiting: on an interval or a cron (a **manifest**), once
later, after another manifest succeeds, or when something queues a run. Every run goes through one
work queue, a dispatcher hands it to a job submitter, and a worker runs it and records it like any
other run. [Scheduling](https://traxsharp.net/docs/scheduler) explains the parts, and
[Schedule a train](https://traxsharp.net/docs/scheduler/setup) builds a host from nothing.

## Decide first

1. **Which database.** This decides most of what works.

   | Provider | What the scheduler does there |
   |---|---|
   | `UsePostgres()` | Everything, across any number of processes: the ManifestManager, the job dispatcher, local workers, standalone workers, metadata cleanup. |
   | `UseSqlite()` | The full scheduler, in **one process**. Writes take the file's lock, so the local worker pool starts one worker unless you set `WorkerCount`. Nothing coordinates a second process on the same file. |
   | `UseInMemory()` | Tests and prototypes only. No job dispatcher and no work queue reader: the in-memory ManifestManager runs due manifests inline. Retry delays, decision replay and the stale-run reapers do not run, a GraphQL `mode: QUEUE` is refused with `TRAX_QUEUE_UNAVAILABLE`, and a machine cannot invoke a train. |

   More than one Trax process against the same data (a hub and workers, two replicas) needs
   Postgres. [SQLite and Postgres](https://traxsharp.net/docs/effect/sqlite-and-postgres) has the
   comparison. The `trax-scheduler` and `trax-hub` templates start on `UseInMemory()`;
   [Switch a template to Postgres](https://traxsharp.net/docs/reference/switch-a-template-to-postgres)
   before relying on queues, retries or dead letters.

2. **Where trains run.** Pick the topology before writing `Program.cs`;
   [Deployment topologies](https://traxsharp.net/docs/reference/deployment-topologies) lists five,
   each with its registrations and a sample.

   | Shape | Registration |
   |---|---|
   | One host schedules and runs | `AddScheduler()` on a database provider. Local workers start by default. |
   | A hub that only schedules, workers that only run | Hub: `OverrideSubmitter(s => s.AddScoped<IJobSubmitter, PostgresJobSubmitter>())`, which starts no local workers. Workers: `services.AddTraxWorker()` with `AddTrax`, `UsePostgres` and `AddMediator`. |
   | Some trains elsewhere | `UseRemoteWorkers`, `UseSqsWorkers` (`Trax.Scheduler.Sqs`) or `UseLambdaWorkers` (`Trax.Scheduler.Lambda`), routed per train with `ForTrain<T>()` or `[TraxRemote]`. Untouched trains stay local. |
   | No scheduler at all | Trains run inline in the request. Nothing here applies. |

   [Choosing where trains run](https://traxsharp.net/docs/scheduler/remote-execution#which-model-should-i-use)
   compares the remote models.

3. **Code or runtime.** Manifests declared on the builder are seeded at every start, upserted by
   external id. Manifests created at run time through `ITraxScheduler` live only in the database.
   A host that declares any manifest in code deletes, at its next start, every manifest it owns that
   the code does not declare. So a host that also creates manifests at run time calls
   `PruneOrphanedManifests(false)`
   ([Orphan manifest cleanup](https://traxsharp.net/docs/scheduler/orphan-manifest-cleanup)).

## Steps

1. Give the train an input type that implements `IManifestProperties` (the scheduler stores it),
   and register the train with `AddMediator`. `Schedule<T>` refuses a train the mediator did not
   register.
2. Register the scheduler after `AddEffects` and `AddMediator`; `AddScheduler` only compiles on
   the builder `AddMediator` returns. Add the liveness check where an orchestrator probes the host:

   ```csharp compile
   using Trax.Effect.Data.Postgres.Extensions;
   using Trax.Effect.Extensions;
   using Trax.Mediator.Extensions;
   using Trax.Scheduler.Extensions;
   using Trax.Scheduler.Services.Scheduling;

   builder.Services.AddTrax(trax =>
       trax.AddEffects(effects => effects.UsePostgres(connectionString))
           .AddMediator(assembly)
           .AddScheduler(scheduler =>
               scheduler
                   .Schedule<IExtractOrdersTrain>(
                       "extract-orders",
                       new ExtractOrdersInput(),
                       Every.Minutes(5),
                       options => options.MaxRetries(3)
                   )
                   .ThenInclude<ILoadOrdersTrain>("load-orders", new LoadOrdersInput())
                   .Schedule<INightlyReportTrain>(
                       "nightly-report",
                       new NightlyReportInput(),
                       Cron.Daily(hour: 3)
                   )
           )
   );

   builder.Services.AddHealthChecks().AddTraxSchedulerLiveness();
   ```

3. Change a global option only after reading its range in
   [Value Ranges](https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges).
   A value outside it stops the host at build, with every problem listed.
4. At run time inject `ITraxScheduler`: `TriggerAsync(externalId)` runs a manifest now and leaves
   its schedule alone, `TriggerAsync(externalId, delay)` runs it later, `ScheduleOnceAsync` makes a
   one-off that disables itself, `CancelAsync` cancels its runs, and `RequeueDeadLetterAsync` or
   `AcknowledgeDeadLetterAsync` resolve a dead letter
   ([ITraxScheduler](https://traxsharp.net/docs/sdk-reference/scheduler-api/i-trax-scheduler)).
   To queue one run of a train with no manifest, use `ITrainExecutionService.QueueAsync`
   ([ITrainExecutionService](https://traxsharp.net/docs/sdk-reference/mediator-api/i-train-execution-service)).
5. Run the host and read its startup log. A scheduler that cannot run refuses to start, and
   [Startup errors](https://traxsharp.net/docs/scheduler/startup-errors) lists each message.

## Option ranges worth knowing

| Option | Range | Default |
|---|---|---|
| `PollingInterval`, `ManifestManagerPollingInterval`, `JobDispatcherPollingInterval` | 1 second to 30 days | 5 s manifest manager, 2 s dispatcher |
| `DefaultJobTimeout`, `StalePendingTimeout`, `StaleInProgressTimeout` | 1 second to 10 years | 20 min, 20 min, 60 min |
| `DefaultRetryDelay`, `MaxRetryDelay` | 0 to 10 years | 5 min, 1 hour |
| `RetryBackoffMultiplier` | finite, at least 1 | 2.0 |
| `ConfigureLocalWorkers`: `WorkerCount` | 1 to 256 | processor count; 1 on SQLite |
| `ConfigureLocalWorkers`: `PollingInterval` | above 0, up to 30 days | 1 s |
| `ConfigureLocalWorkers`: `VisibilityTimeout` | 1 second to 10 years | 30 min |

The polling services never wait less than one second between cycles. The dispatcher also wakes as
soon as work is queued (on Postgres from any host, on SQLite from the same process), so a shorter
poll buys nothing.

## Known mistakes

- **A sub-second dispatcher interval.** `JobDispatcherPollingInterval(TimeSpan.FromMilliseconds(500))`
  refuses to start. Leave the default, or use one second or more.
- **Testing retries on the in-memory provider.** It has no retry delay and no reapers, so the test
  proves nothing. Run retry and dead-letter tests on Postgres
  ([How retries work](https://traxsharp.net/docs/scheduler/how-retries-work)).
- **Two processes on one SQLite file.** Each runs its own ManifestManager and dispatcher with
  nothing between them. Use Postgres for a second process.
- **A worker pool twice.** `AddTraxWorker()` beside an `AddScheduler()` that already runs local
  workers is refused. Tune the scheduler's pool with `ConfigureLocalWorkers` instead, or make the
  hub schedule-only with `OverrideSubmitter`.
- **`[TraxRemote]` with nowhere to send it.** A scheduler with a `[TraxRemote]` train and no
  `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` refuses to start; it does not fall back
  to running the train locally.
- **Expecting a code change to win.** Settings saved from the operations console or the
  `updateScheduler` mutation are stored in `trax.scheduler_config`, and a stored value beats the
  builder value on every host until it is changed or the row is deleted. The log warns when one
  replaces a builder value ([Stored settings](https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler#stored-settings)).
- **Changing `DefaultMaxRetries` for existing jobs.** It applies to manifests created after the
  change. State `MaxRetries` on the manifest to have its code value written at every start.
- **Renaming an external id.** It creates a new manifest, and the next start prunes the old one with
  its run history.
- **Setting `SchedulerConfiguration` properties from application code.** Their setters are hidden
  from completion and are for tests (the
  [E2E page](https://traxsharp.net/docs/cross-cutting/e2e-testing#choose-how-the-scheduler-runs-under-test)
  turns the ManifestManager off this way). Configure through the builder or the operations surface.
- **A machine that invokes a train, with no scheduler anywhere.** Entering the state only queues the
  run. A scheduler on some host sharing the database must dispatch it, or no outcome ever arrives.
  `trax-design-a-machine` covers the rest.

## Several hosts on one Postgres

- The ManifestManager runs on one host at a time, the one holding a Postgres advisory lock. The
  dispatcher runs on every host and claims work queue rows with row locks, so `MaxActiveJobs` is
  counted per host: N hosts can reach N times the limit
  ([Concurrency](https://traxsharp.net/docs/scheduler/concurrency)).
- A fleet on Trax.Effect 1.57.2 or earlier takes a different lock key from newer hosts. Stop every
  host before starting the first upgraded one
  ([Upgrade schedulers from Trax.Effect 1.57.2 or earlier](https://traxsharp.net/docs/migration-guides/scheduler-leader-lock)).
- Point the liveness probe at the health endpoint, so a scheduler that stops dispatching is
  restarted ([Scheduler liveness](https://traxsharp.net/docs/scheduler/health-and-liveness)).
- With remote workers, raise `MaxConcurrentDispatch` (default 1), or each dispatch waits for the
  previous HTTP call.

## Read next

- [How the scheduler decides when a manifest runs](https://traxsharp.net/docs/scheduler/scheduling-options):
  misfires, variance, exclusions, groups, timeouts.
- [Dependent trains](https://traxsharp.net/docs/scheduler/dependent-trains) and
  [Run a job once, later](https://traxsharp.net/docs/scheduler/delayed-jobs).
- [Resolve a dead letter](https://traxsharp.net/docs/scheduler/dead-letters-and-cleanup).
- [Run trains on Lambda](https://traxsharp.net/docs/scheduler/run-trains-on-lambda) and
  [Run trains on SQS workers](https://traxsharp.net/docs/scheduler/run-trains-on-sqs-workers).
- [9. Scheduled work](https://traxsharp.net/docs/book/scheduled-work): the book adds a scheduler to
  a running app.
- Testing a scheduler host: `trax-test-your-app`.
