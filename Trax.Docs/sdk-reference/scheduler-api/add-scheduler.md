---
layout: default
title: AddScheduler
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 1
---

# AddScheduler

Adds the Trax.Core scheduler subsystem. Registers `ITraxScheduler`, the background polling service, and all scheduler infrastructure. Provides a `SchedulerConfigurationBuilder` lambda for configuring global options, execution backends, and startup schedules.

## Signature

```csharp
// With configuration
public static TraxBuilderWithMediator AddScheduler(
    this TraxBuilderWithMediator builder,
    Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
)

// Parameterless defaults
public static TraxBuilderWithMediator AddScheduler(
    this TraxBuilderWithMediator builder
)
```

`AddScheduler` is called on `TraxBuilderWithMediator` (the return type of `AddMediator()`), which enforces at compile time that effects and the mediator are configured before the scheduler.

The parameterless overload registers the scheduler with default settings, equivalent to `AddScheduler(scheduler => scheduler)`.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder>` | No | Lambda that receives the scheduler builder and returns it after configuring options, execution backends, and schedules. Omit for defaults. |

## Returns

`TraxBuilderWithMediator`, for continued fluent chaining (adding another `AddScheduler()` call is not typical, but the type allows further configuration).

## Example

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .ManifestManagerPollingInterval(TimeSpan.FromSeconds(5))
        .JobDispatcherPollingInterval(TimeSpan.FromSeconds(5))
        .MaxActiveJobs(50)
        .DefaultMaxRetries(5)
        .DefaultRetryDelay(TimeSpan.FromMinutes(2))
        .RetryBackoffMultiplier(2.0)
        .MaxRetryDelay(TimeSpan.FromHours(1))
        .DefaultJobTimeout(TimeSpan.FromMinutes(20))
        .DefaultMisfirePolicy(MisfirePolicy.FireOnceNow)
        .DefaultMisfireThreshold(TimeSpan.FromSeconds(60))
        .RecoverStuckJobsOnStartup()
        .DependentPriorityBoost(16)
        .AddMetadataCleanup()
        .Schedule<MyTrain>(
            "my-job",
            new MyInput(),
            Every.Minutes(5),
            options => options
                .Priority(10)
                .Group("my-group"))
    )
);
```

## SchedulerConfigurationBuilder Options

These methods are available on the `SchedulerConfigurationBuilder` passed to the `configure` lambda:

### Execution Backend

| Method | Description |
|--------|-------------|
| [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) | Customizes the built-in PostgreSQL local workers (enabled by default with Postgres) |
| [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | Routes specific trains to a remote HTTP endpoint for execution |
| [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | Routes specific trains to an Amazon SQS queue for execution (`Trax.Scheduler.Sqs`) |
| [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | Offloads synchronous `run` execution to a remote endpoint (blocks until complete) |
| `OverrideSubmitter(Action<IServiceCollection>)` | Registers a custom job submitter implementation |

### Global Options

| Method | Parameter | Default | Description |
|--------|-----------|---------|-------------|
| `PollingInterval(TimeSpan)` | interval | 5 seconds | Shorthand that sets both `ManifestManagerPollingInterval` and `JobDispatcherPollingInterval` to the same value |
| `ManifestManagerPollingInterval(TimeSpan)` | interval | 5 seconds | How often the ManifestManager evaluates manifests and writes to the work queue |
| `JobDispatcherPollingInterval(TimeSpan)` | interval | 2 seconds | How often the JobDispatcher reads from the work queue and dispatches to the job submitter |
| `SchedulerLivenessThreshold(TimeSpan)` | threshold | max(JobDispatcherPollingInterval * 10, 30s) | How long the dispatcher may go without completing a cycle before [`AddTraxSchedulerLiveness()`](/docs/scheduler/health-and-liveness) reports unhealthy |
| `MaxConcurrentDispatch(int)` | maxConcurrent | 1 | Max entries dispatched concurrently per polling cycle. Increase when using `UseRemoteWorkers` to avoid sequential HTTP blocking. See [Parallel Dispatch](/docs/scheduler/admin-trains/job-dispatcher#parallel-dispatch) |
| `MaxDispatchAttempts(int)` | maxAttempts | 5 | Max dispatch attempts before permanently failing a work queue entry. When a job was not delivered, the entry is requeued after a backoff (5 s, doubling, up to 5 min), and that attempt's run does not count toward `MaxRetries`; a job a runner already started is never requeued. Set to 0 to disable requeuing (fail immediately). See [Dispatch failures](/docs/scheduler/admin-trains/job-dispatcher#dispatch-failures) |
| `MaxActiveJobs(int?)` | maxJobs | 10 | Max concurrent active jobs (Pending + InProgress) globally. `null` = unlimited. Approximate: each dispatching host counts on its own, so N hosts can reach N times the limit. Per-group limits can also be set from the dashboard on each ManifestGroup |
| `MaxQueuedJobsPerCycle(int?)` | limit | 100 | Max queued work queue entries loaded per JobDispatcher cycle. Prevents unbounded memory usage when the queue is large. `null` = unlimited. Provides headroom beyond `MaxActiveJobs` for per-group limit skipping |
| `MaxWorkQueueEntriesPerCycle(int?)` | limit | 200 | Max work queue entries created per ManifestManager cycle, distributed fairly across manifest groups (`limit / numGroups` per group, overflow to higher-priority groups). Prevents a single large group from starving smaller groups. `null` = unlimited |
| `ExcludeFromMaxActiveJobs<TTrain>()` | _(none)_ | _(none)_ | Excludes a train type from the MaxActiveJobs count |
| `DefaultMaxRetries(int)` | maxRetries | 3 | Retries after the first run before dead-lettering (the default allows four attempts), for manifests that don't set `MaxRetries`. A runtime change applies to manifests seeded after it (every manifest at the next start) |
| `FailureCountWindow(TimeSpan)` | window | 24 hours | How far back a manifest's failed runs count toward its retry backoff and its `MaxRetries`. A failure older than the window no longer delays the next run or counts toward a dead letter. Throws `ArgumentOutOfRangeException` unless between one second and ten years. Readable and patchable at runtime through `IOperationsService`; the persisted settings row does not store it yet, so a patched value applies only to the host that received it and lasts until that host restarts |
| `DefaultRetryDelay(TimeSpan)` | delay | 5 minutes | Base delay between retries |
| `RetryBackoffMultiplier(double)` | multiplier | 2.0 | Exponential backoff multiplier. Set to 1.0 for constant delay |
| `MaxRetryDelay(TimeSpan)` | maxDelay | 1 hour | Caps retry delay to prevent unbounded growth |
| `DefaultJobTimeout(TimeSpan)` | timeout | 20 minutes | Timeout after which a run is cancelled when its manifest sets no `Timeout`, and for runs with no manifest |
| `DefaultMisfirePolicy(MisfirePolicy)` | policy | `FireOnceNow` | Default [misfire policy](/docs/scheduler/scheduling-options#misfire-policies) for manifests that don't specify one. A runtime change applies to manifests seeded after it |
| `DefaultMisfireThreshold(TimeSpan)` | threshold | 60 seconds | Grace period before misfire policies take effect. If a manifest is overdue by less than this, it fires normally |
| `RecoverStuckJobsOnStartup(bool)` | recover | `true` | Whether to auto-recover stuck jobs on startup |
| `DeadLetterRetentionPeriod(TimeSpan)` | retention | 30 days | How long a resolved dead letter is kept before the automatic purge deletes it |
| `AutoPurgeDeadLetters(bool purge = true)` | purge | `true` | Whether resolved dead letters past the retention period are deleted automatically. Read on every purge run, so it can also be changed at runtime |
| `StalePendingTimeout(TimeSpan)` | timeout | 20 minutes | Timeout after which a Pending job that was never picked up is automatically failed |
| `StaleInProgressTimeout(TimeSpan)` | timeout | 60 minutes | Timeout after which an InProgress job that never completed is automatically failed. Acts as a safety net for hard crashes (Lambda kills, OOM) where FinishServiceTrain never runs. Should be longer than `DefaultJobTimeout` to allow cooperative cancellation to propagate first. A run whose manifest sets a longer `Timeout` is failed only at that timeout plus the same grace (`StaleInProgressTimeout - DefaultJobTimeout`) |
| `StaleStagedEntryTimeout(TimeSpan)` | timeout | 10 minutes | How long a work queue entry staged by a train with `DeferQueuePromotion` may stay unconfirmed before the ManifestManager resolves it. An entry still unconfirmed after this long belongs to a process that stopped between the two commits. Keep it well above the slowest `OnQueue` hook, because an entry resolved while its hook is still running is resolved wrongly. The sweep runs in the ManifestManager, so nothing resolves stale entries while `ManifestManagerEnabled` is false |
| `PromoteStaleStagedEntries(bool promote = true)` | `promote` | off (cancel) | Promotes stale unconfirmed entries instead of cancelling them. Cancelling is the default because nothing recorded tells a hook that succeeded from one that never ran or one that rejected the mutation. Opt in only when every deferring train's chain re-checks what its hook checked and every hook is idempotent. `Trax.Docs/adr/0018` records why |
| `PruneOrphanedManifests(bool)` | prune | `true` | Whether to [delete manifests](/docs/scheduler/orphan-manifest-cleanup) from the database that are no longer defined in the startup configuration. Disable if you create manifests dynamically at runtime via `ITraxScheduler`. A host that declares no manifests prunes nothing, and a manifest with a pending or running run is kept until the run finishes |
| `DependentPriorityBoost(int)` | boost | 16 | Priority boost added to dependent train work queue entries at dispatch time. Range: 0-31. Dependent trains are dispatched before non-dependent ones by default |

### Startup Schedules

| Method | Description |
|--------|-------------|
| [Schedule](/docs/sdk-reference/scheduler-api/schedule) | Schedules a single recurring train (seeded on startup) |
| [ScheduleMany](/docs/sdk-reference/scheduler-api/schedule-many) | Batch-schedules manifests from a collection |
| [Then / ThenMany](/docs/sdk-reference/scheduler-api/dependent-scheduling) | Schedules dependent trains |
| [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) | Enables automatic metadata purging |

## Remarks

- The settings the dashboard's Server Settings page and the [`updateScheduler`](/docs/sdk-reference/graphql-api/mutations#config-nested-namespace) mutation edit (the enable switches, polling intervals, `MaxActiveJobs`, retry, timeout and dead-letter settings, the local worker count and the metadata cleanup interval and retention) are stored in `trax.scheduler_config` once an operator changes one. The scheduler applies the stored row over these builder values at startup and re-reads it every few seconds while running, so a change saved on any host, including an API-only one, applies without a restart; the local worker count is the exception and applies when the worker pool next starts. A stored value takes precedence over the builder value until the row is changed or deleted. `FailureCountWindow` is not stored yet: a runtime change to it stays on the host that received it, and the row never resets it. See [scheduler ADR 0010](https://github.com/TraxSharp/Trax.Scheduler/blob/main/docs/adr/0010-a-settings-save-writes-only-what-it-names-and-every-scheduler-applies-it.md).
- `AddScheduler` requires `AddEffects()` and `AddMediator()` to be called first. This is enforced at compile time -- `AddScheduler` is only available on `TraxBuilderWithMediator`, which is the return type of `AddMediator()`.
- `AddScheduler` requires a data provider (`UsePostgres()` or `UseInMemory()`). If no data provider is configured, `AddScheduler` throws `InvalidOperationException` at build time with a helpful error message showing the required configuration.
- Internal scheduler trains (`ManifestManager`, `InMemoryManifestManager`, `JobDispatcher`, `JobRunner`, `MetadataCleanup`, `DeadLetterCleanup`) are automatically excluded from `MaxActiveJobs`.
- With `UseInMemory()`, `JobDispatcherPollingService` and `MetadataCleanupPollingService` are not registered. The `ManifestManagerPollingService` runs an `InMemoryManifestManagerTrain` that dispatches jobs inline through the in-memory job submitter.
- Manifests declared via `Schedule`/`ScheduleMany` are not created immediately. They are seeded on application startup by the `SchedulerStartupService`.
- Manifests declared via `Schedule`/`ThenInclude`/`Include` get a ManifestGroup based on their `groupId` parameter (defaults to externalId). Per-group dispatch controls (MaxActiveJobs, Priority, IsEnabled) are configured from the dashboard.
- At build time, the scheduler validates that ManifestGroup dependencies form a DAG (no circular dependencies). If a cycle is detected, `AddScheduler` throws `InvalidOperationException` with the groups involved. See [Dependent Trains: Cycle Detection](/docs/scheduler/dependent-trains#cycle-detection).
