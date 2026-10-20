---
title: Deployment topologies
description: "The five ways to split a Trax application across processes, what each process registers, and the sample that runs each one."
type: explanation
area: scheduling
nav_order: 2
---

# Deployment topologies

The same trains can be wrapped by different executables, and the executables are where the
deployment choices live: which process schedules, which one runs the trains, which one serves
GraphQL, and how lifecycle events get from the process that ran a train to the one serving the
subscription. The trains do not change between topologies; only the `Program.cs` files do. How to
put the trains in a library that several executables share is in
[Structuring your application](/docs/reference/project-layout#trains-in-a-class-library).

Four of the five topologies below have a sample that runs them, and the sample is the evidence
that the registrations described work together. The second one has no sample.

## One host does everything

Samples: [Scheduling](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Scheduling), [Recovery](/docs/samples/recovery).

One ASP.NET process schedules trains, runs them, serves GraphQL and, in Development, serves the
operations console. It runs them on the scheduler's local workers, which `AddScheduler()` registers by
default whenever the effects have a database provider: a `PostgresJobSubmitter` writes each job to
`background_job` and worker threads in the same process claim it. The Recovery sample adds
subscriptions on the same host; its page listens to `onJunctionEvent` for live progress. Nothing
crosses a process, so no broadcaster is needed.

The process registers `AddScheduler()` and `AddTraxGraphQL()`, and maps `UseTraxApiDashboard()`.

This is where most applications start, and it holds until execution has to scale apart from the
API, or a long job must not compete with requests for the same CPU.

## A separate API and scheduler

Sample: none. No sample runs this topology, so the registrations below are not exercised together
by any end-to-end suite.

Two processes share the trains and the Postgres database. The scheduler process runs background
work; the API process serves GraphQL and the operations console, which reads everything over it,
runs light trains inline and queues heavy ones. Each calls `UseBroadcaster(b => b.UseRabbitMq(...))`, because a train the scheduler
runs must reach the API's subscriptions.

The API needs a job submitter to queue anything. `AddTraxGraphQL()` refuses to start a host that
calls `ExposeOperationMutations()` with no `IJobSubmitter` registered, because `runTrain` hands
every run to one. The startup error names the two ways out: call `AddScheduler(...)` in the API
too, or, for an API that schedules nothing, register `AddMediator(...)`, `AddTraxJobRunner()`, the
`IOperationsService` and `services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()` yourself, so
the jobs the API writes are the ones the scheduler process's workers read.

It suits an API that must stay responsive while background jobs run elsewhere, without the cost of
a fleet of workers.

## A hub and distributed workers

Sample: [Energy Hub](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/DistributedWorkers). The book splits Shopfront this way in [11. A separate worker](/docs/book/a-separate-worker).

The hub schedules and serves the API but executes nothing it queues.
`OverrideSubmitter(s => s.AddScoped<IJobSubmitter, PostgresJobSubmitter>())` registers the
submitter that writes jobs to `background_job` and no local workers; without the override, a
scheduler on Postgres runs trains too. Separate worker processes call `AddTraxWorker()`, claim jobs
with `FOR UPDATE SKIP LOCKED`, and scale horizontally: start another and it shares the queue.
Workers publish lifecycle events over RabbitMQ, so the hub's subscriptions see runs that finished
elsewhere.

It suits high throughput, and execution that scales independently of scheduling.

## Ephemeral workers

Sample: [Content Shield](/docs/samples/content-shield).

The API runs no trains at all. `UseRemoteWorkers()` posts queued jobs to a runner, and
`UseRemoteRun()` sends every synchronous run there too, queries included. The runner is a
`TraxLambdaFunction`: an AWS Lambda function in production, and in development `RunLocalAsync()`
serving `/trax/execute` and `/trax/run` over HTTP. Both sides share a signing key (see the runner's
[authorization posture](/docs/scheduler/remote-execution#authorization-posture)); outside
Development a missing key stops startup. No `background_job` table is involved. The runner
publishes lifecycle events over RabbitMQ, so the API's subscriptions see completions.

It suits serverless deployments and on-demand workloads that should cost nothing while idle. The
price is a network hop on every run, including the synchronous ones.

## A single server with domain subscriptions

Sample: [Chat Service](/docs/samples/chat-service).

No scheduler and no workers: every train runs inline in the request that asked for it. A custom
`ITrainLifecycleHook`, registered through its factory with `AddLifecycleHook`, fires for every
train that completes (custom hooks do not depend on `[TraxBroadcast]`) and publishes a domain event
to a custom `onChatEvent` field that extends `LifecycleSubscriptions`, Trax's subscription root.
Sockets authenticate in `connection_init`, and the field admits only a room's participants.

It suits chat, collaboration and notification feeds driven by train results.

## Side by side

| | One host | Separate API | Hub and workers | Ephemeral | Single server |
|---|---|---|---|---|---|
| Sample | Scheduling, Recovery | none | Energy Hub | Content Shield | Chat Service |
| Processes | 1 | 2 | 2 or more | 2 | 1 |
| Scheduler | in-process | scheduler process | hub, scheduling only | API, dispatch only | none |
| Execution | local workers | scheduler process | workers, polling | runner, HTTP push | inline |
| API | in-process | API process | hub | API | in-process |
| Operations console (Development) | in-process | API process | hub | API | none |
| Job table | `background_job` | `background_job` | `background_job` | none | none |
| Horizontal scaling | no | no | workers | runner | no |
| Subscriptions | built-in, junction events | over RabbitMQ | over RabbitMQ | over RabbitMQ | custom field fed by a lifecycle hook |

The registrations each topology uses are documented on their SDK pages:
[AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler),
[AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker),
[AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner),
[UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers),
[UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run),
[TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function),
[UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster),
[AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) and
[AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql).
