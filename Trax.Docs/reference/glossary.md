---
layout: default
title: Glossary
description: Definitions of the names Trax uses for trains and junctions, effects and recording, the mediator and scheduling, each with the package that holds it.
parent: Reference
nav_order: 6
---

# Glossary

The names Trax uses, in the code and in these pages. Each entry says where the type lives; [Packages](/docs/reference/packages) says which package to install for it.

## Trains and junctions

| Term | What it means |
|---|---|
| **Train** | `Train<TIn, TOut>` in `Trax.Core.Train`. A class that takes one input, runs the chain its `Junctions()` method declares, and returns either the output or the exception that stopped it. See [Trains and Junctions](/docs/core/trains-and-junctions). |
| **ServiceTrain** | `ServiceTrain<TIn, TOut>` in `Trax.Effect.Services.ServiceTrain`. A train that records each run as a **Metadata** row and runs the registered **effects** around it. It implements `IServiceTrain<TIn, TOut>`, and its `Run` is sealed, so what runs is always the declared chain. The base class for trains the mediator, scheduler and API can run. |
| **Route** | `IRoute<TIn, TOut>` in `Trax.Core.Route`: anything with `Run(input, cancellationToken)` that produces an output or fails. Every train is a route. `AddScopedTraxRoute<IMyTrain, MyTrain>()` registers a train under its interface. |
| **Junction** | `Junction<TIn, TOut>` in `Trax.Core.Junction`. One step of a chain: its `Run(TIn)` returns `Task<TOut>`. Trax builds a junction through its single public constructor, taking each argument from **Memory** first and the DI container second. |
| **EffectJunction** | `EffectJunction<TIn, TOut>` in `Trax.Effect.Services.EffectJunction`. A junction whose runs the junction effects (the junction logger, junction progress) can see. It works only inside a ServiceTrain and throws a `TrainException` in any other train. |
| **Chain** | The sequence of junctions a train declares in `Junctions()`, written with `Chain<TJunction>()`, `ShortCircuit<TJunction>()` and `Resolve()`. See [Building Chains](/docs/core/building-chains). |
| **Chain verification** | The check `AddMediator` registers: at host startup it replays every registered train's chain and refuses to start when one cannot run, naming the train, the step and the missing type. See [Troubleshooting](/docs/cross-cutting/troubleshooting). |
| **Memory** | The values a running train holds, keyed by type. It starts with the train's input, and each junction's output is added to it. A junction's input, and each constructor argument Memory can supply, is taken from it by type. See [Memory](/docs/core/memory). |
| **Right track / left track** | A chain's two outcomes, as an `Either<Exception, T>` from LanguageExt. Right carries the value on to the next junction; left carries the exception past every remaining junction to the train's result. |
| **ShortCircuit** | A chain step that ends the train early with its output when it succeeds, and lets the chain continue when it throws. See [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit). |

## Effects and recording

| Term | What it means |
|---|---|
| **Effect** | Something that runs alongside every ServiceTrain run without being part of its chain. An effect provider (`IEffectProvider` in `Trax.Effect.Services.EffectProvider`) tracks the run's models and persists them when the train saves. Effects are added on the `AddEffects` builder: a data provider, `AddJson()`, `SaveTrainParameters()`. See [Effect Providers](/docs/effect/effect-providers). |
| **Junction effect** | An effect that runs before and after each EffectJunction rather than around the whole train: `AddJunctionLogger()` and `AddJunctionProgress()`. |
| **Data provider** | The effect that stores runs: `UsePostgres(...)`, `UseSqlite(...)` or `UseInMemory()`, one package each. The scheduler, junction progress and state machines refuse to start without one. |
| **Metadata** | `Metadata` in `Trax.Effect.Models.Metadata`, a row of `trax.metadata`. One per ServiceTrain run: the train's name, its `TrainState` (`Pending`, `InProgress`, `Completed`, `Failed`, `Cancelled`), start and end times, input and output when `SaveTrainParameters()` stores them, and on failure the junction, exception, reason and stack trace. `ParentId` links a run to the run that started it. See [Metadata](/docs/effect/metadata). |
| **ExternalId** | A string id meant for use outside the database. On a **Manifest** it is the stable name the scheduling APIs key on: scheduling the same ExternalId again updates that manifest instead of adding one. On a **Metadata** row it is typically a GUID by which other systems refer to the run. |
| **Lifecycle hook** | An `ITrainLifecycleHook`, registered with `AddLifecycleHook`. Trax calls its `OnStarted`, `OnCompleted` and `OnFailed` as a run changes state; an exception it throws is logged and never fails the train. See [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook). |
| **Broadcaster** | `UseBroadcaster(...)`: publishes train lifecycle events to other processes through a transport such as RabbitMQ, or to browsers through SignalR. See [Broadcaster Sinks](/docs/effect/broadcaster-sinks). |
| **Log** | A row of `trax.log`: one `ILogger` message captured by `AddDataContextLogging`. See [Debugging with the Log Table](/docs/effect/debugging-with-the-log-table). |

## Mediator

| Term | What it means |
|---|---|
| **Mediator** | What `AddMediator(...)` registers: it discovers the trains in the assemblies you name, so callers can run a train without referencing its class. See [Mediator](/docs/mediator). |
| **TrainBus** | `ITrainBus` in `Trax.Mediator.Services.TrainBus`. `RunAsync<TOut>(input)` finds the train registered for the input's type and runs it. When none is registered it throws `NoTrainForInputException`. See [TrainBus](/docs/sdk-reference/mediator-api/train-bus). |

## Scheduling

| Term | What it means |
|---|---|
| **Manifest** | `Manifest` in `Trax.Effect.Models.Manifest`, a row of `trax.manifest`. A job definition: which train to run, the input to run it with, when (cron, interval, once, after a parent manifest succeeds, or only when triggered), and its retry and timeout policy. Each run of a manifest creates a Metadata row. See [Scheduling](/docs/scheduler). |
| **IManifestProperties** | The marker interface in `Trax.Effect.Models.Manifest` (package `Trax.Effect`, not `Trax.Scheduler`) that a scheduled train's input type implements, so the scheduler can store the input on the manifest and rebuild it for each run. The type must round-trip through `System.Text.Json`. |
| **Manifest group** | A row of `trax.manifest_group`: settings shared by the manifests in it, `MaxActiveJobs` (how many may run at once), `Priority` (dispatch order between groups) and `IsEnabled`. See [Concurrency](/docs/scheduler/concurrency). |
| **Work queue** | `trax.work_queue`: runs that have been asked for but not yet dispatched. Every source of a run (a due manifest, a trigger, a dashboard re-run) adds an entry, and the job dispatcher takes entries from it. An entry is `Queued`, `Dispatched` or `Cancelled`. |
| **Background job** | A row of `trax.background_job`: a dispatched run waiting for a local worker, which claims the row, runs the train and deletes the row. |
| **Dead letter** | A row of `trax.dead_letter`, created when a manifest's failed runs exceed its `MaxRetries`. It waits as `AwaitingIntervention` until an operator retries it (`Retried`) or acknowledges it (`Acknowledged`). See [Dead Letters](/docs/scheduler/dead-letters-and-cleanup). |
| **Manifest manager** | The scheduler's own train that runs each cycle: it cancels timed-out jobs, reaps stale and failed runs into dead letters, and turns due manifests into work queue entries. See [Manifest Manager](/docs/scheduler/admin-trains/manifest-manager). |
| **Job dispatcher** | The scheduler's own train that takes queued work queue entries, applies the global and per-group limits, and hands each one to a job submitter. See [Job Dispatcher](/docs/scheduler/admin-trains/job-dispatcher). |
| **Job runner** | The scheduler's own train that runs one dispatched job: it loads the job's Metadata and manifest, then runs the train through the TrainBus. See [Job Runner](/docs/scheduler/admin-trains/job-runner). |
| **Local worker / remote worker** | Where a dispatched job runs. Local workers are threads in the scheduler's own process, the default with Postgres or SQLite. Remote workers are other processes reached over HTTP, SQS or AWS Lambda. See [Remote Execution](/docs/scheduler/remote-execution). |
| **Dependent train** | A manifest with `ScheduleType.Dependent`: rather than running on a timer, it runs after its parent manifest's latest success. See [Dependent Trains](/docs/scheduler/dependent-trains). |
