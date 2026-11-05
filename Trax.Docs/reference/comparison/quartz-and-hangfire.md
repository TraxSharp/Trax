---
title: Trax compared with Quartz.NET and Hangfire
description: "How the Trax scheduler compares with Quartz.NET 4 and Hangfire 1.8, and when to choose each."
type: explanation
area: general
nav_order: 1
---

# Trax compared with Quartz.NET and Hangfire

Checked on 2026-11-05 against Quartz.NET 4.7 and Hangfire 1.8.25. Corrections are welcome as issues.

All three run background work in .NET. They solve different problems.

[Quartz.NET](https://www.quartz-scheduler.net/) is a time-based job scheduler. Its strength is precise triggers:
cron with seconds, calendars, daily time windows, time zones and daylight saving. Version 4.0 (September 2026,
.NET 10 only) added retry policies, stored execution history, continuations, typed job input, an HTTP API and a
dashboard. Version 3.x, without them, is still maintained for older frameworks. It is Apache-2.0.

[Hangfire](https://www.hangfire.io/) is a background job processor. Its strength is simplicity: pass a lambda, and the
method runs in the background with retries, persistence and a mature dashboard. Hangfire is LGPL-3.0; batches,
official Redis storage and throttling are in the paid Pro and Ace packages.

Trax's [scheduler](/docs/scheduler) is one layer of a larger framework. It runs trains, chains of typed steps whose
every run is recorded, on cron and interval schedules, and it needs the layers below it. The rest of Trax (an API, the
operations console, state machines) is optional.

## Scheduling

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Cron | 5 or 6 fields (seconds) | 5, 6 or 7 fields (seconds, optional year) | 5 or 6 fields, or `Cron.*` helpers |
| Time zones | UTC only | Per trigger, and a default | Per recurring job |
| Daylight saving | Not applicable: UTC | Calendar-interval triggers keep the hour of day | Cron in a chosen time zone, DST-aware |
| Intervals | `Every.Minutes(5)` | Simple, calendar-interval, daily-time-interval and fixed-delay triggers | No interval type: cron such as `*/5 * * * *` |
| Exclusions | `Exclude.DaysOfWeek()`, `Dates()`, `DateRange()`, `TimeWindow()` | Calendars: holiday, weekly, daily, cron, annual, monthly | No |
| Run a job now, with any input | `ITrainExecutionService.QueueAsync(...)`; `TriggerAsync(externalId)` for an existing schedule | `TriggerJob(jobKey, input)` | `BackgroundJob.Enqueue(() => ...)` |
| Run once, later | `ScheduleOnceAsync(input, delay)` or `TriggerAsync(id, delay)` | `ScheduleJob<TJob, TInput>(input, delay)` | `BackgroundJob.Schedule(..., delay)` |
| Missed runs | Per schedule: `FireOnceNow` or `DoNothing`, past a threshold | Misfire instructions per trigger type | `Relaxed`, `Strict` or `Ignorable` per recurring job |
| Priority | 0 to 31 per group and per queued run; dependents get a boost | Per trigger, compared only between triggers due at the same time | Queue order; honoured only with Pro's Redis storage |
| Pause a schedule | Disable a schedule or a whole group, in code or the console | Pause and resume a trigger, a job, a group or everything | Not documented |
| Schedules removed from code | Deleted at the next start | Kept until you delete them | Kept until `RecurringJob.RemoveIfExists` |
| Many schedules at once | `ScheduleMany`, `IncludeMany`, at startup or at runtime | `ScheduleJobs(...)` in one transaction | Batches in Pro (paid) |

Trax evaluates every schedule and exclusion in UTC. A job that has to run at 9:00 in a local time zone, across daylight
saving changes, is a better fit for Quartz.NET or Hangfire. A Trax schedule is picked up on the scheduler's next poll,
every 5 seconds by default, so firings closer together than that are not precise. Queued work starts sooner: on
PostgreSQL a notification wakes every dispatcher, with a 2-second poll behind it.

## Composition

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Unit of work | A train (`IServiceTrain<TIn, TOut>`) made of `Junction<TIn, TOut>` steps | `IJob`, or `IJob<TInput>` | Any public method |
| Steps inside a job | A chain of junctions; the first failure skips the rest | None built in | None built in |
| Typed input | `IServiceTrain<TInput, TOutput>` | `IJob<TInput>`; `JobDataMap` in 3.x | Compile-time lambda arguments |
| Jobs that follow jobs | `Include()` and `ThenInclude()`: one parent per schedule; groups checked for cycles at startup | `StartAfter` continuations with outcome conditions; one parent each | `ContinueJobWith`; Pro batches add a continuation after a whole batch |
| A parent choosing its children at runtime | Yes: dormant dependents, activated with input the parent chooses | No | No |
| "If nothing happens in 30 minutes" | A state machine timer, stored and fired by the server, up to 366 days, which can start a train | A one-off trigger | `BackgroundJob.Schedule` |

A Quartz or Hangfire job is one method. A Trax train splits the same work into junctions, each its own class with its
own dependencies and tests, and records which junction failed.

## Persistence and history

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Databases | PostgreSQL; SQLite for a single process; in-memory | RAM, SQL Server, PostgreSQL, MySQL, Oracle, Firebird, SQLite (no clustering) | SQL Server and in-memory; Redis in Pro (paid); PostgreSQL, MongoDB, MySQL, SQLite and Redis from the community |
| Run history | Every run's state, timing, failing junction and exception; input and output too with `SaveTrainParameters()` | A row per run with result, exception message and duration; input opt-in (4.4+) | Every state change; the job's arguments and return value |
| After the last retry | A dead letter in `trax.dead_letter`, acknowledged or requeued from the console | The trigger returns to its schedule, or pauses | `FailedState`, kept until retried or deleted from the dashboard |
| Retention | Your trains' runs are kept until you add them to `AddMetadataCleanup`; resolved dead letters go after 30 days | A retention per result, 1 day by default | Succeeded and deleted jobs expire; failed ones never do |
| Logs per run | `ILogger` output written to `trax.log`, tied to the run, readable in the console | Execution log capture, capped (4.3) | The community `Hangfire.Console` |

## Running on several servers

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Several servers | Yes, on PostgreSQL | Yes, on a clustered database store | Yes |
| Coordination | One server evaluates schedules under an advisory lock; workers claim runs with `FOR UPDATE SKIP LOCKED` | No leader: a database lock per trigger acquisition, check-ins to detect dead nodes | Distributed locks and atomic queue fetch |
| Capacity | Global `MaxActiveJobs` (10 by default) and a cap per manifest group, approximate across several hosts; workers per host | Thread pool size, and execution-group limits per node or cluster (4.6+) | Workers per server and per queue; semaphores in Ace (paid) |
| One run at a time per subject | For queued work: one run per subject key at a time, across servers, the next chosen by priority then age | Not covered here | `[Mutex]` in Ace (paid); `[DisableConcurrentExecution]` locks a whole method |
| Fair dispatch | Each manifest group gets a share of every dispatch cycle, so a busy group cannot starve the rest | Not found | Not found |
| Work on other machines | HTTP runners, SQS or AWS Lambda, chosen per train, with signed requests | Clustered nodes on a shared database | Servers sharing the storage |
| Hosts | The console lists each host, its role, its slots in use and the runs it holds | Not covered here | Not covered here |

## Retries and failures

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Automatic retry | Scheduled runs: 3 by default. A run queued on demand is not retried | `[RetryPolicy]` or a default policy (4.4+) | Every job: 10 by default |
| Backoff | Exponential: 5 minutes doubling to 1 hour by default | Fixed, exponential with cap and jitter, or a list | (n-1)⁴ + 15 + random·n seconds, overridable |
| Stuck runs | Recovered on startup; runs stuck Pending past 20 minutes or running past 60 are failed so retries apply | `RequestsRecovery` | The server watchdog requeues |
| Resume part-way | A retry or a requeued dead letter resumes after the train's last checkpoint; an operator can resume from a chosen step | No | No |
| Repeat a run's choices | A retried or requeued run replays the branch decisions the original recorded | No | No |
| Which failures retry | Every failure retries; the failure's class is recorded (transient, conflict, permanent) | Any exception retries | `OnlyOn` and `ExceptOn` filters |

## Cancellation

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| In job code | A `CancellationToken` through the train, its junctions and EF Core | `CancellationToken` on the job context | A `CancellationToken` parameter |
| From code | `CancelAsync(externalId)`, `CancelGroupAsync(groupId)` | `Interrupt(jobKey)` | `BackgroundJob.Delete(jobId)` |
| On another server | Between junctions, through a database flag (needs `AddJunctionProgress()`) | At the owning node's next check-in (4.6+) | The job's state is polled every 5 seconds |
| From the UI | Cancel a run or a whole group | Interrupt a running job (4.7) | Delete |
| Recorded as | `Cancelled`, excluded from retries, dead letters and the success rate | A `Cancelled` result (4.4+) | `DeletedState` |

## Hooks and progress

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Hooks | `ITrainLifecycleHook`: started, completed, failed, cancelled, state changed | Job, trigger and scheduler listeners | Client, server and state filters |
| Progress | The junction running now, by name, with `AddJunctionProgress()` | A percentage and message, `ReportProgress` | No |
| Live updates | GraphQL subscriptions; SignalR and RabbitMQ broadcasters | Listeners | SignalR through community packages |

## Dashboard

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Dashboard | The operations console at `/trax/dashboard`, served by the API host | Blazor at `/quartz`, or standalone; 3.x has none | Built-in middleware at `/hangfire` |
| API | GraphQL, opt-in per train with `[TraxQuery]` and `[TraxMutation]` | An HTTP API at `/quartz-api` | Community extensions |
| Actions | Run or queue any train with new input; trigger, enable, disable or edit a schedule; cancel; requeue a run or a dead letter; resume from a checkpoint; change scheduler settings on every host without a restart; do any of these to everything a filter matches | Pause, resume, trigger with overrides, interrupt, run again, backfill, a cron editor | Requeue, delete, reschedule |
| Terminal and scripts | `trax-console`: the same lists and actions, and JSON lines for scripts | The HTTP API | Not found |
| A failure | Reason, failing junction, failure class and stack trace | The exception message | Stack trace |
| Numbers | Executions per minute or hour, queue depth, success rate, each run's junction timeline | Runs over time by result, median and p95 duration | Succeeded and failed counts, queue depth |
| Access | Refuses to start until you choose `RequirePolicy`, `RequireRoles` or `AllowAnonymousShell()` | Refuses to start until you choose `RequireAuthorization()` or `AllowAnonymous()` | Local requests only, until you add an authorization filter |

## Security

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| Who may start a job over the API | `[TraxAuthorize]` per train; a served train without it, or without `[TraxAllowAnonymous]`, stops the host | View, Operate and Administer roles per scheduler | No API to start jobs |
| Remote workers | A runner refuses to start without a signing key or an explicit opt-out; replayed requests are refused | Not applicable | Not applicable |
| Audit of operator actions | Every GraphQL request, bulk operations included, sent to an audit sink you write | A pause records its reason and user | Not found |

## Developer experience

| | Trax | Quartz.NET 4 | Hangfire |
|---|---|---|---|
| A first scheduled job | A train, a junction and a schedule, or `dotnet new trax-scheduler` | A job class and its trigger | One line |
| Dependency injection | Constructor injection in trains and junctions | Scoped jobs through the job factory | Scoped services through `JobActivator` |
| Testing | An in-memory data provider; junctions tested alone | `JobExecutionContextBuilder`, a fake time provider | Replace services in DI |
| Checked before the first run | The host refuses to start if a chain can never run, naming the step and the missing type | Analyzers and a generator (4.2+) | None |
| Health check | `AddTraxSchedulerLiveness()`: unhealthy once the dispatcher stops completing cycles | `AddQuartz` health checks | Community packages |
| Licence | MIT | Apache-2.0 | LGPL-3.0; Pro and Ace are paid |

Hangfire has the least ceremony. Trax has the most: reaching a scheduled job means trains and junctions (Core), run
recording (Effect), dispatch (Mediator) and manifests (Scheduler).

## When to choose Trax

- **Scheduled work has phases.** Extract, transform, validate and load become junctions, each testable alone and each
  named in the run record when it fails.
- **Schedules depend on each other.** Parent and child schedules are declared at startup, groups are checked for
  cycles, and a parent can decide at runtime which dormant children run and with what input.
- **You need an audit trail.** State, timing and failure details are recorded for every run once a data provider is
  configured, and `SaveTrainParameters()` adds input and output.
- **You run work in bulk.** `ScheduleMany` and manifest groups with their own caps keep a thousand sync jobs from
  starving billing.
- **You want dead letters.** A scheduled run that runs out of retries waits for a person instead of retrying forever.
- **Work for one record must not overlap.** Queued work with the same subject key runs one at a time across servers.
- **Failed work should restart where it stopped.** A retry or an operator resumes after the last checkpoint instead of
  rerunning steps that already called other systems.
- **Jobs run on Lambda, SQS or separate worker services.** Each train is routed to a transport, and what is sent is
  signed.
- **Operators fix things in production.** Edit schedules, pause them, run a train with new input, and requeue or cancel
  everything a filter matches, from a browser or a terminal.
- **Callers outside the process start jobs.** Trains are GraphQL mutations with authorization per train, and the host
  refuses to start with one left unguarded.
- **You already use Trax.** The scheduler runs the trains you have.

## When to choose Quartz.NET or Hangfire

- **The job is "send this email in five minutes".** Hangfire does it in one line.
- **Schedules follow a local clock.** Trax evaluates cron and exclusions in UTC only.
- **Your database is SQL Server, MySQL or Redis.** Quartz.NET covers SQL Server and MySQL, Hangfire covers SQL Server,
  and Hangfire Pro covers Redis. Trax runs on PostgreSQL, or SQLite for one process.
- **Every job needs retries, including ones started on demand.** Hangfire retries every job; Trax retries scheduled
  runs only.
- **You want the lowest learning curve,** or background work is a small part of the system.
- **You need OpenTelemetry tracing.** Quartz.NET 4 emits spans and metrics; Trax does not emit spans.
- **You only need a scheduler.** Trax.Scheduler needs Trax.Effect and Trax.Mediator under it. Quartz.NET and Hangfire
  stand alone.

## Using them together

Trax does not need to own every background job in an application. Hangfire or Quartz.NET can keep the jobs they already
run while trains take over the work that has internal structure. A Hangfire job or a Quartz `IJob` resolves
`ITrainBus` and calls `RunAsync` with the train's input, so the run is recorded in Trax like any other, while the
outer library keeps its own trigger, retry and dashboard. Trax's own retries, dead letters and misfire rules do not
apply to those runs, since they are not Trax schedules, and a Hangfire retry reruns the whole train. Use
`ITrainExecutionService.QueueAsync` instead of `RunAsync` to queue the run rather than run it inline.
