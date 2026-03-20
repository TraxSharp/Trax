---
layout: default
title: Job Submission
parent: Scheduling
nav_order: 2
---

# Job Submission

The job submitter is the execution backend for the scheduler. When the JobDispatcher creates a Metadata record and calls `IJobSubmitter.EnqueueAsync()`, the job submitter is responsible for picking up that job and running the train.

## Built-in Local Workers (PostgreSQL)

The recommended backend uses Trax.Core's own `trax.background_job` table for job queuing. No external dependencies — it shares the same PostgreSQL database already used by Trax.Core's data layer.

The JobDispatcher commits the Metadata creation and WorkQueue status update in a `FOR UPDATE SKIP LOCKED` transaction before calling `EnqueueAsync`. The BackgroundJob insertion then happens as a separate operation. This ordering ensures the Metadata record is visible to the job submitter when it begins execution — necessary because the `InMemoryJobSubmitter` executes synchronously within the `EnqueueAsync` call.

### How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                     JobDispatcherTrain                        │
│  Creates Metadata → Calls EnqueueAsync(metadataId)               │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                     PostgresJobSubmitter                          │
│  INSERT INTO trax.background_job (metadata_id, ...)       │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                   background_job table                           │
│  ┌────┬─────────────┬───────┬────────────┬────────────┐         │
│  │ id │ metadata_id │ input │ created_at │ fetched_at │         │
│  ├────┼─────────────┼───────┼────────────┼────────────┤         │
│  │ 1  │     42      │ null  │ 10:00:00   │   null     │ ← available
│  │ 2  │     43      │ {...} │ 10:00:01   │ 10:00:05   │ ← claimed
│  └────┴─────────────┴───────┴────────────┴────────────┘         │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                   LocalWorkerService                               │
│  N concurrent workers polling the table                          │
│                                                                  │
│  Worker 0 ──► SELECT ... FOR UPDATE SKIP LOCKED ──► Execute     │
│  Worker 1 ──► SELECT ... FOR UPDATE SKIP LOCKED ──► Execute     │
│  Worker 2 ──► SELECT ... FOR UPDATE SKIP LOCKED ──► Execute     │
│              (each worker gets a different job — no duplicates)  │
└─────────────────────────────────────────────────────────────────┘
```

### Setup

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        // Local workers enabled automatically with Postgres — no extra call needed
        .Schedule<IMyTrain, MyInput>(
            "my-job", new MyInput(), Every.Minutes(5))
    )
);
```

No connection string parameter needed — local workers use the same `IDataContext` already registered by `UsePostgres()`.

### Configuration

```csharp
.ConfigureLocalWorkers(options =>
{
    options.WorkerCount = 4;                                   // default: Environment.ProcessorCount
    options.PollingInterval = TimeSpan.FromSeconds(2);         // default: 1 second
    options.VisibilityTimeout = TimeSpan.FromMinutes(30);      // default: 30 minutes
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);        // default: 30 seconds
    options.BatchSize = 5;                                     // default: 1
})
```

| Option | Default | Description |
|--------|---------|-------------|
| `WorkerCount` | `Environment.ProcessorCount` | Number of concurrent worker tasks polling for jobs |
| `PollingInterval` | 1 second | How often idle workers poll for new jobs |
| `VisibilityTimeout` | 30 minutes | How long a claimed job stays invisible before crash recovery reclaims it |
| `ShutdownTimeout` | 30 seconds | Grace period for in-flight jobs during application shutdown. When the host signals shutdown, in-flight trains receive the cancellation token after this delay — giving them time to finish cleanly. See [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens#background-services-and-shutdown). |
| `BatchSize` | 1 | Number of jobs each worker claims per poll. Higher values reduce database round-trips when there is a backlog. Claimed jobs are processed sequentially within the worker task. If a worker crashes mid-batch, uncompleted jobs wait for `VisibilityTimeout` before being reclaimed. |

### Worker Lifecycle

Each worker runs a three-phase loop:

**Phase 1 — Claim** (atomic, within a transaction)

```sql
SELECT * FROM trax.background_job
WHERE fetched_at IS NULL
   OR fetched_at < NOW() - make_interval(secs => :visibility_timeout)
ORDER BY created_at ASC
LIMIT 1
FOR UPDATE SKIP LOCKED
```

The `FOR UPDATE SKIP LOCKED` clause ensures:
- Each job is claimed by exactly one worker (no duplicates)
- Workers don't block each other (SKIP LOCKED, not WAIT)
- Oldest jobs are processed first (ORDER BY created_at ASC)

On claim, the worker sets `fetched_at = NOW()` and commits the transaction.

**Phase 2 — Execute** (in a fresh DI scope)

The worker resolves `IJobRunnerTrain` from a new DI scope and calls `Run(new RunJobRequest(metadataId, input))`. This is the same train that Hangfire previously invoked — it loads the Metadata, validates the job state, executes the target train, and updates the Manifest's `LastSuccessfulRun` on success.

**Phase 3 — Cleanup** (always runs, success or failure)

The worker deletes the `background_job` row. This matches the previous Hangfire behavior where jobs were auto-deleted on completion. Trax.Core's Metadata and DeadLetter tables handle the audit trail — the background_job table is purely a transient queue.

### Crash Recovery

If a worker crashes after claiming a job (Phase 1) but before deleting it (Phase 3), the `fetched_at` timestamp becomes stale. The dequeue query's `WHERE fetched_at < NOW() - :visibility_timeout` condition makes the job eligible for re-claim by another worker after the visibility timeout expires.

```
Worker A claims job #1 → fetched_at = 10:00:00
Worker A crashes at 10:01:00
                               ...
At 10:30:00 (30m later):
Worker B's dequeue query finds job #1 eligible (fetched_at < NOW() - 30m)
Worker B claims and executes job #1
```

This is the same pattern Hangfire uses with its `InvisibilityTimeout` — a well-established approach for reliable background job processing.

### Comparison with Hangfire

| Feature | Hangfire | PostgresJobSubmitter |
|---------|----------|-------------------|
| **Dependencies** | 3 NuGet packages (Hangfire.Core, Hangfire.AspNetCore, Hangfire.PostgreSql) | None (uses existing EF Core) |
| **Database tables** | 10+ tables in `hangfire` schema | 1 table in `trax` schema |
| **Retries** | Disabled (Trax.Core manages retries) | N/A (Trax.Core manages retries) |
| **Recurring jobs** | Not used | N/A (ManifestManager handles scheduling) |
| **Concurrency** | Thread-based workers | Task-based workers |
| **Job storage** | Separate connection/schema | Same `IDataContext` as all Trax.Core data |
| **Dashboard** | Hangfire Dashboard (separate UI) | Trax.Core Dashboard |
| **Migration** | Hangfire manages its own schema | DbUp migration alongside other Trax.Core tables |
| **Crash recovery** | InvisibilityTimeout | VisibilityTimeout (same pattern) |

## Hangfire (Removed)

> **Removed**: Local workers are now the default with Postgres. The `UseHangfire()` method and `Trax.Scheduler.Hangfire` package have been removed. If migrating from an older version, see below.

The Hangfire backend wraps Hangfire's `IBackgroundJobClient.Enqueue()` to dispatch jobs. It brings 3 NuGet packages and creates its own database tables, but Trax.Core only uses a tiny fraction of Hangfire's capabilities:

- One API call (`Enqueue`)
- Retries disabled
- Auto-delete on completion
- No recurring jobs, continuations, or batches

If you're using Hangfire and need to migrate, see [Migrating from Hangfire](#migrating-from-hangfire).

## InMemory Workers

When `UseInMemory()` is configured (no PostgreSQL), the scheduler automatically uses `InMemoryJobSubmitter`. This executes jobs immediately and synchronously — no background workers, no database tables. The `EnqueueAsync` call blocks until the train completes.

This is useful for testing and local development where you want the scheduler API without database infrastructure:

```csharp
// UseInMemory() → InMemoryJobSubmitter is used automatically
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UseInMemory())
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

## Remote Execution

The default setup runs everything on one process. For production deployments, you can separate scheduling from execution — offloading train execution to dedicated worker servers, AWS Lambda, or other compute.

Trax supports two remote execution models:

**Push-based (HTTP)** — the scheduler POSTs to a remote endpoint:

```csharp
// Scheduler side:
.AddScheduler(scheduler => scheduler
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://my-workers.example.com/trax/execute",
        routing => routing.ForTrain<IMyTrain>())
)

// Remote side:
builder.Services.AddTraxJobRunner();
app.UseTraxJobRunner("/trax/execute");
```

Best for serverless compute (Cloud Run, Azure Functions) where the remote process only runs when invoked.

**Queue-based (SQS + Lambda)** — the scheduler sends messages to an SQS queue, Lambda consumes them:

```csharp
using Trax.Scheduler.Sqs.Extensions;

// Scheduler side:
.AddScheduler(scheduler => scheduler
    .UseSqsWorkers(
        sqs => sqs.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs",
        routing => routing.ForTrain<IMyTrain>())
)

// Lambda side: use SqsJobRunnerHandler (see Trax.Scheduler.Sqs)
```

Best for AWS Lambda with durable message delivery, automatic retries, and dead-letter queues. Requires the `Trax.Scheduler.Sqs` package.

**Poll-based (standalone worker)** — a separate process polls the `background_job` table:

```csharp
builder.Services.AddTraxWorker(opts => opts.WorkerCount = 4);
```

Best for dedicated worker servers that run continuously and scale independently.

Both models connect to the same Postgres database. See [Remote Execution](/docs/scheduler/remote-execution) for full architecture details, deployment diagrams, and guidance on which model to choose.

## Custom Job Submitter

Implement `IJobSubmitter` and register it via `OverrideSubmitter()`:

```csharp
public class MyJobSubmitter : IJobSubmitter
{
    public Task<string> EnqueueAsync(long metadataId) { /* ... */ }
    public Task<string> EnqueueAsync(long metadataId, object input) { /* ... */ }
}

// Registration
.AddScheduler(scheduler => scheduler
    .OverrideSubmitter(services =>
    {
        services.AddScoped<IJobSubmitter, MyJobSubmitter>();
    })
)
```

## Migrating from Hangfire

### 1. Update Configuration

```diff
  builder.Services.AddTrax(trax => trax
      .AddEffects(effects => effects
          .UsePostgres(connectionString)
      )
      .AddScheduler(scheduler => scheduler
-         .UseHangfire(connectionString)
+         // Local workers are now the default — no call needed
          .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
      )
  );

- // Remove Hangfire dashboard
- app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [] });
```

### 2. Update Package References

```diff
  <ItemGroup>
      <PackageReference Include="Trax.Scheduler" />
-     <PackageReference Include="Trax.Scheduler.Hangfire" />
  </ItemGroup>
```

### 3. Remove Hangfire Usings

```diff
- using Hangfire;
- using Trax.Scheduler.Hangfire.Extensions;
```

### 4. Run the Application

The `trax.background_job` table is created automatically by the migration system on startup. No manual SQL required.

### 5. Clean Up Hangfire Tables (Optional)

After confirming the migration works, you can drop the Hangfire schema:

```sql
DROP SCHEMA IF EXISTS hangfire CASCADE;
```

## SDK Reference

> [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) | [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [OverrideSubmitter](/docs/sdk-reference/scheduler-api/add-scheduler)
