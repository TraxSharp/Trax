---
title: Choosing where trains run
description: "The models for running trains off the scheduler host (HTTP runners, SQS, direct Lambda, standalone workers), what each costs and how they fail."
type: explanation
area: scheduling
nav_order: 10
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/JobSubmitter
  - Trax.Scheduler/src/Trax.Scheduler/Services/RunExecutor
---

# Choosing where trains run

By default the scheduler schedules, dispatches and runs trains in one process. Remote execution
separates **where a train is scheduled** from **where it runs**, so the code can run on a worker
fleet, in AWS Lambda, on ECS tasks or on other compute while scheduling stays where it is.

## Postgres stays the source of truth

Every model connects to the same database for manifests, the work queue, run metadata and dead
letters. The only thing that moves is where the train's code runs.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                         Shared PostgreSQL                               │
│  trax.manifest    trax.metadata    trax.work_queue    trax.dead_letter  │
└──────────────────────────────┬───────────────────────────────────────────┘
                               │
        ┌──────────────┬───────┴───────┬────────────────┬────────────────┐
        │              │               │                │                │
  Local Workers  Remote Workers  Lambda Workers   SQS Workers    Standalone Workers
  (same process) (HTTP push)     (direct SDK)     (SQS + Lambda) (separate process)
```

Two seams decide where work goes. Queued work (scheduled manifests, triggers, `queue*` mutations)
leaves the JobDispatcher through a job submitter, described in
[How jobs reach workers](/docs/scheduler/job-submission). Synchronous work (`run*` mutations and
`[TraxQuery]` queries) goes through a run executor: `LocalRunExecutor` runs it in-process through
`ITrainBus.RunByNameAsync`, and [`UseRemoteRun`](/docs/sdk-reference/scheduler-api/use-remote-run)
or [`UseLambdaRun`](/docs/sdk-reference/scheduler-api/use-lambda-run) send it to a runner and wait
for the answer. Each of those two replaces the executor, so the last one configured wins.

## Deployment Models

### Model 1: Local Workers

Everything runs in one process: the ManifestManager writes the work queue, the JobDispatcher hands
each run to `PostgresJobSubmitter`, and `LocalWorkerService` threads take it from
`trax.background_job` and run it. It is the default with Postgres and needs no call.

```
┌─────────────────── Single Process ───────────────────┐
│  ManifestManager ──→ WorkQueue ──→ JobDispatcher      │
│                                        ▼              │
│                              background_job table      │
│                                        ▼              │
│                              LocalWorkerService       │
│                              └─→ JobRunner ─→ Your Train │
└───────────────────────────────────────────────────────┘
```

There are no network hops and nothing extra to deploy or debug. Most applications never need more,
and the others can start here and move out the trains that need it.

### Model 2: Remote Workers (Push-Based)

The scheduler POSTs each job to an HTTP endpoint on a runner, which runs the train and answers. The
runner is an ASP.NET Core host with `AddTraxJobRunner`, `UseTraxJobRunner` for queued jobs and
`UseTraxRunEndpoint` for synchronous runs. Setup is on
[UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers),
[UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) and
[AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner).

```
┌──── Scheduler Process ────┐         ┌──── Remote Process ────────────┐
│  ManifestManager           │         │  POST /trax/execute            │
│  JobDispatcher             │  HTTP   │       ▼                        │
│  HTTP submitter ───────────┼────────→│  JobRunner ─→ Your Train      │
└────────────────────────────┘  POST   └────────────────────────────────┘
                                                │
                                       Shared PostgreSQL
```

It fits compute that only runs when invoked (Cloud Run, Azure Functions, a Lambda behind a Function
URL), trains that must run inside another security boundary or VPC, trains that need different
hardware, and runners that scale apart from the scheduler.

Two consequences are easy to miss. `UseRemoteRun` takes **every** synchronous run off the scheduler
process, `RUN` mutations and `[TraxQuery]` queries alike, because both go through the run executor;
a host that wants its queries answered locally registers `UseRemoteWorkers` alone. And a runner in
another process can only tell the API host that a queued train finished through a broadcaster, so a
runner that should feed GraphQL subscriptions calls
[UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster).

On Lambda, the HTTP model needs a publicly routable Function URL or API Gateway. Direct invocation
(Model 2c) avoids that. The [Content Shield sample](/docs/samples/content-shield) runs this model:
its API sends queued mutations and synchronous runs to a runner over signed HTTP, with RabbitMQ
carrying completions back.

### Model 2b: SQS Workers (Queue-Based, AWS Lambda)

Like Model 2, with a durable SQS queue between the scheduler and the workers. The scheduler sends
each job to the queue, and a Lambda function consumes it through `SqsJobRunnerHandler`. The queue
adds what direct HTTP lacks: guaranteed delivery, redelivery, a dead-letter queue and backpressure,
so a burst is buffered and drained at the rate Lambda allows. It needs the `Trax.Scheduler.Sqs`
package; setup, including the `ReportBatchItemFailures` setting the event source mapping needs, is
on [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers).

```
┌──── Scheduler ────┐        ┌──── SQS ────┐       ┌── Lambda ──────────────┐
│  JobDispatcher     │  Send  │  trax-jobs   │ Event │  SqsJobRunnerHandler   │
│  SQS submitter ────┼───────→│  queue       │──────→│  └─→ JobRunner ─→ Train │
└────────────────────┘        └──────────────┘       └────────────────────────┘
```

SQS delivers at least once, so the design leans on the run's metadata row rather than the queue: a
record whose train ran is acknowledged whether the train succeeded or failed, because its failure
is already on the run's row and the manifest's retries act on it. Only a record the runner refused
or could not start is handed back for redelivery. The SQS transport has no sample yet and is not
considered production ready; for Lambda, Model 2c is the recommended path.

### Model 2c: Lambda Workers (Direct Invocation)

The scheduler invokes a Lambda function through the AWS SDK. There is no API Gateway, Function URL
or HTTP endpoint at all, and access is governed by IAM: the scheduler needs
`lambda:InvokeFunction` on the function. The function derives from `TraxLambdaFunction`, which
receives a `LambdaEnvelope` and dispatches on its type. It needs the `Trax.Scheduler.Lambda` package
on the scheduler and `Trax.Runner.Lambda` in the function; setup is on
[UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers),
[UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) and
[TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function).

```
┌──── Scheduler ────────────┐  SDK    ┌──── AWS Lambda ─────────────────┐
│  JobDispatcher             │ Invoke  │  TraxLambdaFunction             │
│  Lambda submitter ─────────┼────────→│  └─→ ITraxRequestHandler ─→ Train│
└────────────────────────────┘         └─────────────────────────────────┘
```

Queued jobs are invoked asynchronously (`Event`): the scheduler gets a 202 and the function runs
on its own. Synchronous runs use `RequestResponse` and wait for the output. That difference sets the
payload limit: an asynchronous invocation carries far less than the 6 MB a synchronous one may, so
a large input is better stored elsewhere and passed by reference. For local development,
`RunLocalAsync()` serves the same `/trax/execute` and `/trax/run` routes over HTTP, so a developer
uses `UseRemoteWorkers` and `UseRemoteRun` locally and the Lambda calls in production, as the
[Content Shield sample](/docs/samples/content-shield) does.

### Model 3: Standalone Workers (Poll-Based)

A separate, always-on process runs `AddTraxWorker` and takes jobs from the same `background_job`
table the local workers use, with the same `FOR UPDATE SKIP LOCKED` claim, so any number of them
can run side by side. The scheduler host either keeps its own local workers alongside them or
registers `PostgresJobSubmitter` through `OverrideSubmitter`, which writes the table without
starting any. Setup is on [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker).

```
┌──── Scheduler ────────────┐         ┌──── Standalone Worker ─────────┐
│  JobDispatcher             │         │  LocalWorkerService            │
│  PostgresJobSubmitter      │         │  SELECT ... FOR UPDATE         │
│  background_job ───────────┼─────────┼──→ JobRunner ─→ Your Train     │
└────────────────────────────┘ same DB └────────────────────────────────┘
```

It fits dedicated machines, workers that scale horizontally, deployments where a scheduler crash
must not kill running trains, and Kubernetes or ECS services scaled on their own.

Two things stay on the scheduler. A standalone worker has no API to register an authorization
service on, and only runs work that passed the check where it was submitted, so a worker with
`[TraxAuthorize]` trains declares that with `AllowMissingAuthorizationService()` (see
[Authorization: Opting Out for Scheduler-Only Hosts](/docs/authorization#opting-out-for-scheduler-only-hosts)).
And synchronous work is still answered by the process that receives the request: a `[TraxQuery]`
or a `RUN` mutation does not reach a worker, so mutations that should always run on one are
declared `GraphQLOperation.Queue`. The [Energy Hub sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/DistributedWorkers) runs every
queued job on a standalone worker, and its E2E suite proves the hub has no `LocalWorkerService`.

## Which Model Should I Use?

| Scenario | Model |
|----------|-------|
| One server | Local Workers |
| Separate worker servers that are always running | Standalone Workers |
| AWS Lambda | Lambda Workers: direct invocation, no public endpoint, IAM-governed |
| AWS Lambda with a durable queue in front | SQS Workers |
| Cloud Run or Azure Functions | Remote Workers |
| Different hardware for some trains | Remote Workers, routed per train |
| Just getting started | Local Workers |

Models mix. Routing is per train: `ForTrain<T>()` on a `UseRemoteWorkers`, `UseSqsWorkers` or
`UseLambdaWorkers` call, or `[TraxRemote]` on the train, sends it to that runner, and every train
not routed runs locally. Each call keeps its own options, client and signing key, so trains can go
to different runners. A train routed by two calls is refused when the scheduler is built. A
`[TraxRemote]` train no call routes explicitly goes to the first routed registration, and a
scheduler with `[TraxRemote]` trains and no routed registration refuses to start, naming them:
running a train marked remote on the one host it was kept off is not a fallback.

## Authorization Posture

A runner runs what it is sent as trusted infrastructure: the scheduler already authorized the
work, so the runner skips per-train `[TraxAuthorize]` checks. That moves the trust question to the
runner's entry points, and every one of them refuses to start until it is told who may send it
work: a signing key shared with the scheduler, an ASP.NET policy that admits only the scheduler, or
an explicit, logged `AllowUnsignedRequests()` for a runner nothing else can reach. A signed request
is checked before its body is read, and a runner accepts only the input types of its own registered
trains. [Runner authorization posture](/docs/scheduler/runner-authorization) describes each posture,
what is checked on each transport and where nonces are kept.

## Host Tracking

Every run records which host executed it: hostname, environment (Lambda, ECS, Kubernetes and
others) and instance id, in every model and with no configuration. See
[Host Tracking](/docs/effect/host-tracking).

## Shared Requirements

Every process that runs trains must:

1. **Reference the same train assemblies**, because trains and their inputs are resolved by full
   name, and register them with `AddMediator`.
2. **Connect to the same database**, since manifests, metadata and the work queue are shared.
3. **Register the effect system**: `AddTrax()` with the data provider and `AddMediator()`.

## Failure Handling

The JobDispatcher commits a run's metadata row before it submits the job, because the runner has to
read that row. A submit that fails afterwards (a timeout, an unreachable runner, throttling) would
leave the row `Pending` with nothing running it, so the scheduler has several layers that each
catch a different way of getting there. The [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher#dispatch-failures)
owns the details and the settings.

### Delivery and execution

A failed submit is not always a failed delivery. The HTTP submitter also fails when the runner ran
the job and answered with an error, or is still running it when the HTTP timeout expires. In both
cases the runner owns the run and its outcome is, or will be, on the run's row. So the dispatcher
fails the row only while it is still `Pending`, with one conditional write. A row that has left
`Pending` was started by a runner: the entry stays dispatched, no dispatch attempt is counted and
the train is not run again. The same rule covers a job delivered twice, by an SQS redelivery, a
retried Lambda invocation or a re-claimed local job: only the delivery that moves the row out of
`Pending` runs the train.

### 1. Retry with Exponential Backoff

The HTTP and Lambda submitters and run executors retry transient failures themselves, with
exponential backoff and jitter, before the dispatcher sees a failure: HTTP 429, 502 and 503
(honouring `Retry-After`), and for Lambda 429, 502, 503, 504 and network errors. The limits are
`HttpRetryOptions` on each transport's options; see
[UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers).

### 2. Dispatch Requeue

A submit that still fails, for a job no runner started, puts the entry back in the queue with a
backoff, and the attempt's run is recorded as `DispatchRequeued`, which does not count against the
manifest's retries. A short outage of a runner therefore does not dead-letter anything. After
`MaxDispatchAttempts` the last attempt counts as one failure.

### 3. Stale Pending Reaper

A run still `Pending` after `StalePendingTimeout` is failed by the ManifestManager: the runner
received it and died before starting, or never received it at all.

### 4. Stale InProgress Reaper

A run still `InProgress` long after its timeout is failed too: the worker died without reaching
the end of the train, as a Lambda hard kill or an out-of-memory crash does. Both reapers are
described on the [ManifestManager](/docs/scheduler/admin-trains/manifest-manager#reapstalependingmetadatajunction).

### 5. Dead-Lettering

What the reapers and the dispatcher record are ordinary failed runs, so the manifest's retries and
dead letter act on them like any other failure. See [How retries work](/docs/scheduler/how-retries-work).

### Errors across the boundary

A runner never sends a stack trace or an arbitrary exception message back: the detail stays in the
runner's log and on the run's row, which the runner writes itself. What crosses is a structured
response with the exception's type, the failing junction, the failure class the runner's
classifier assigned, and a public message only for a plain `TrainException`, whose message a train
author wrote for the caller. The class is carried rather than recomputed because the exception type
it was computed from no longer exists on the calling side. A client of the API sees the public
message or "The train failed.", never the runner's internals. The fields are listed in
[Remote error responses](/docs/scheduler/remote-error-responses), and
[Debug a failed remote run](/docs/scheduler/debug-remote-failures) is the order to look in.

## Limitations

- **Cancelling a remote run goes through the database.** Dashboard "Cancel" (and `CancelAsync`) sets the run's persisted cancel flag and also cancels the token of a run on the same process through the in-memory `ICancellationRegistry`. A remote run sees only the flag, at its next junction boundary, and only if the worker registers `CancellationCheckProvider` (added by [`AddJunctionProgress()`](/docs/sdk-reference/configuration/add-junction-progress)). A junction already running on the worker is not interrupted. See [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens).
- **Type resolution requires shared assemblies.** The remote process must reference the same NuGet packages and assemblies that define your train types, and register them with `AddMediator`. A queued job's input type is matched by fully-qualified name against the registered trains' input types, and a remote run's output is read into the output type the caller expects. When that type is an interface or abstract, the output is read into the implementation the runner names, but only if that implementation is already loaded in the scheduler's process and implements the expected type; the scheduler never loads a type by the name the runner sends, and refuses the run otherwise. So the scheduler must reference the assembly that defines the concrete output too.

## SDK Reference

> [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) | [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) | [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | [AddTraxJobRunner / UseTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers) | [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | [OverrideSubmitter](/docs/sdk-reference/scheduler-api/add-scheduler)
