---
layout: default
title: SDK Reference
nav_order: 13
has_children: true
---

# SDK Reference

Complete reference documentation for every user-facing method in Trax. Each page documents the method signature, all parameters, return type, and usage examples.

For conceptual explanations and tutorials, see [Core]({{ site.baseurl }}{% link core.md %}), [Effect]({{ site.baseurl }}{% link effect.md %}), and [Mediator]({{ site.baseurl }}{% link mediator.md %}).

## Categories

### [Train Methods]({{ site.baseurl }}{% link sdk-reference/train-methods.md %}) (Core)

Methods available inside `RunInternal` on `Train<TInput, TReturn>` — the core building blocks for composing steps into a pipeline.

Includes: `Activate`, `Chain`, `ShortCircuit`, `Extract`, `AddServices`, `Resolve`, `Run` / `RunEither`.

### [Configuration]({{ site.baseurl }}{% link sdk-reference/configuration.md %}) (Effect)

The `AddTrax` entry point and every extension method on the step builder types (`TraxBuilder`, `TraxBuilderWithEffects`, `TraxBuilderWithMediator`), `TraxEffectBuilder`, and `TraxEffectBuilderWithData` -- data providers, effect providers, and orchestration setup. The step builder pattern enforces configuration ordering at compile time: Effects -> Mediator -> Scheduler. Data provider methods (`UsePostgres`, `UseInMemory`) return `TraxEffectBuilderWithData`, which unlocks data-dependent methods like `AddDataContextLogging` at compile time.

Includes: `UsePostgres`, `UseInMemory`, `AddJson`, `SaveTrainParameters`, `AddStepLogger`, `AddDataContextLogging`, `AddMediator`, `AddEffect`, `AddStepEffect`, `SetEffectLogLevel`.

### [Mediator API]({{ site.baseurl }}{% link sdk-reference/mediator-api.md %}) (Mediator)

The `ITrainBus` interface for dynamically dispatching trains by input type, plus shared train discovery and execution services.

Includes: `RunAsync`, `InitializeTrain`, `AddMediator`, `ITrainDiscoveryService`, `ITrainExecutionService`.

### [Scheduler API]({{ site.baseurl }}{% link sdk-reference/scheduler-api.md %}) (Scheduler)

Scheduler configuration (`AddScheduler` + `SchedulerConfigurationBuilder`) and the runtime `ITraxScheduler` interface for scheduling, managing, and monitoring recurring trains.

Includes: `AddScheduler`, `UseHangfire`, `Schedule`, `ScheduleMany`, dependent scheduling, manifest management, scheduling helpers (`Every`, `Cron`, `ManifestOptions`).

### [Dashboard API]({{ site.baseurl }}{% link sdk-reference/dashboard-api.md %}) (Dashboard)

Setup and configuration for the Trax Blazor dashboard.

Includes: `AddTraxDashboard`, `UseTraxDashboard`, `DashboardOptions`.

### [GraphQL API]({{ site.baseurl }}{% link sdk-reference/graphql-api.md %}) (API)

GraphQL schema for Trax using HotChocolate. Exposes train discovery, execution (via `[TraxQuery]`/`[TraxMutation]` whitelist), scheduler operations, and read-only queries.

Includes: `AddTraxGraphQL`, `UseTraxGraphQL`, `[TraxQuery]`/`[TraxMutation]` attributes, queries, mutations.
