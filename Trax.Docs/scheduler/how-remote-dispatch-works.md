---
title: How remote dispatch works
description: "What the scheduler's HTTP, SQS and Lambda submitters and run executors do with a run, how a train is routed to one, and the limits of each transport."
type: explanation
area: scheduling
nav_order: 12
---

# How remote dispatch works

A train runs off the scheduler host along one of two paths. A queued run (a manifest coming due, a
`queue*` mutation, `QueueAsync`) leaves through an `IJobSubmitter`, when the JobDispatcher takes
its work queue entry. A synchronous run (a `run*` mutation, a `[TraxQuery]` query,
`ITrainExecutionService.RunAsync`) leaves through the `IRunExecutor`, and its caller waits for the
output. Each remote transport replaces one of the two for the trains it covers, and each has a
receiver on the far side that runs the train against the same database. Which transport to choose
for which train is the subject of [Remote Execution](/docs/scheduler/remote-execution); this page is
about what each one does.

## Routing a queued run

Every queued run goes to the default submitter, which writes a `background_job` row for the local
workers, unless its train is routed elsewhere. A train is routed by, in order:

1. `ForTrain<TTrain>()` in the `routing` callback of a `UseRemoteWorkers`, `UseSqsWorkers` or
   `UseLambdaWorkers` call.
2. `[TraxRemote]` on the train class, which sends a train no call names to the first of those
   calls in the builder, whatever its kind.
3. The default submitter, for everything else.

A train can be routed to one submitter only, and the scheduler's build fails if two calls name it.
It also fails when a `[TraxRemote]` train has none of the three calls to go to: running a train
marked remote on the scheduler host, often the one host it was kept off for isolation, is not
treated as a fallback. Routing never replaces the default submitter, so the local workers keep
running every train no call routes.

Each call is its own submitter, with its own options and client. Two `UseRemoteWorkers` calls to
two endpoints never send one endpoint's credentials, headers or signing key to the other.

## The submitters

All three serialize a `RemoteJobRequest`: the run's metadata id and, when there is one, its input.
The receiver loads the metadata from the shared database, claims the run, runs the train and
records the outcome, so the result never travels back through the submitter.

| | HTTP (`UseRemoteWorkers`) | SQS (`UseSqsWorkers`) | Lambda (`UseLambdaWorkers`) |
|---|---|---|---|
| Delivery | POST to `BaseUrl` | One message to `QueueUrl` | `InvokeAsync` with `InvocationType.Event`, in a `LambdaEnvelope` with `Type = Execute` |
| Waits for | The runner's `RemoteJobResponse` | The send | The invocation to be accepted |
| Job id | `http-{guid}` | `sqs-{messageId}` | `lambda-{guid}` |
| Receiver | `UseTraxJobRunner` | `SqsJobRunnerHandler` | `TraxLambdaFunction` |
| Payload limit | Whatever the runner accepts | 256 KB | 256 KB |
| Retries | `HttpRetryOptions`, on 429, 502, 503 | the SQS client's | `LambdaRetryOptions`, on 429, 502, 503, 504 |

The HTTP submitter reads the runner's response and fails the submit with a `TrainException` on a
non-success status, an `IsError` response, or a success status whose body is not a
`RemoteJobResponse` for the same metadata id: a proxy's page, an empty body, a misrouted `BaseUrl`.

A failed submit is not always a failed delivery. The dispatcher looks at the run's row. If the
runner has already started the run (it ran the train and reported the failure, or is still running
it when the request timed out), the run is the runner's and its outcome is recorded there, so the
entry is not requeued and the train does not run twice. Only a run still `Pending` (the request
never reached a runner, or the runner refused it before starting) counts as a failed dispatch
attempt and is requeued, up to `MaxDispatchAttempts`.

SQS and Lambda both deliver at least once: SQS redelivers by design, and Lambda retries an
asynchronous invocation with the same payload. The run's `Pending` metadata row is what stops a
second delivery from running the train a second time, which is why the receivers check those
requests for their signature but not for freshness or repeats.

## Dispatching many at once

The JobDispatcher dispatches the entries of a cycle one at a time by default. That costs nothing
for the default submitter, whose dispatch is a row insert, but the HTTP submitter waits for the
runner's response, so fifty entries of two seconds each make a hundred-second cycle.
`MaxConcurrentDispatch` dispatches up to that many entries of one cycle at once. Entries are claimed
with `FOR UPDATE SKIP LOCKED`, so concurrent dispatch creates no duplicate runs. Each concurrent
dispatch holds its own DI scope and database connection, so the setting is kept well below the
connection pool size (100 by default with Npgsql). See
[Parallel Dispatch](/docs/scheduler/admin-trains/job-dispatcher#parallel-dispatch).

## The run executors

`UseRemoteRun` and `UseLambdaRun` replace the `IRunExecutor` that `ITrainExecutionService.RunAsync`
runs through, for every train, after the caller has been authorized and the input read on the
calling host. Without either, `LocalRunExecutor` runs the train in process through
`ITrainBus.RunByNameAsync`.

Both serialize a `RemoteRunRequest` (the train name, the input JSON and the input type) and wait
for a `RemoteRunResponse`. On success the output is deserialized and returned. On failure they throw
`RemoteRunException`, a `TrainException`, whose `PublicMessage` is the runner's message for a client
when the failure was a train author's `TrainException`, and null for a transport failure or any
other failure. The receiver (`UseTraxRunEndpoint`, or `TraxLambdaFunction` for a `Run` envelope)
calls `ITrainExecutionService.RunAsync` itself, so the run's metadata is written to the shared
database and appears on the dashboard like a local one.

| | `UseRemoteRun` | `UseLambdaRun` |
|---|---|---|
| Transport | HTTP POST | AWS SDK invocation, `InvocationType.RequestResponse` |
| Public endpoint | Required (a Function URL, API Gateway, or similar) | None; IAM decides who may invoke |
| Package | `Trax.Scheduler` | `Trax.Scheduler.Lambda` |
| Retries | `HttpRetryOptions` | `LambdaRetryOptions` |
| Timeout | `RemoteRunOptions.Timeout`, 5 minutes by default | The function's own timeout, at most 15 minutes |
| Response limit | Whatever the runner returns | 6 MB |

Set beside a submitter, the two paths differ in what the caller gets:

| | Run executor (`UseRemoteRun`) | Submitter (`UseRemoteWorkers`) |
|---|---|---|
| Used for | `run*` mutations, `[TraxQuery]` queries, `RunAsync` | `queue*` mutations, manifests, `QueueAsync` |
| Caller waits | Until the train completes | No; it gets the work queue id at once |
| Caller gets | The train's output | `WorkQueueId` and `ExternalId` |
| Receiver | `UseTraxRunEndpoint()` (`/trax/run`) | `UseTraxJobRunner()` (`/trax/execute`) |
| Default timeout | 5 minutes | 30 seconds |

## Cancelling a remote run

No transport carries a cancel to the receiver. A cancel from the dashboard sets the run's cancel
flag in the database, and the train sees it at its next junction boundary when the receiver
registers `CancellationCheckProvider`, through `AddJunctionProgress()`. A junction already running
is not interrupted. A Lambda function also cancels its own job shortly before the function's
timeout, so the run can record that it was cancelled rather than be left `InProgress` for the
reaper.

## SDK Reference

> [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) | [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) | [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner)
