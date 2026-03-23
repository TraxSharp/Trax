---
layout: default
title: SDK Reference
nav_order: 13
has_children: true
section: SDK Reference
---

# SDK Reference

Complete reference documentation for every user-facing method in Trax. Each page documents the method signature, all parameters, return type, and usage examples.

For conceptual explanations and tutorials, see [Core](/docs/core), [Effect](/docs/effect), and [Mediator](/docs/mediator).

## Categories

### [Train Methods](/docs/sdk-reference/train-methods) (Core)

Methods available on `Train<TInput, TReturn>` for composing junctions. Override `Junctions()` for the standard pattern, or `RunInternal` for advanced cases.

Includes: `Junctions`, `Chain`, `ShortCircuit`, `Extract`, `AddServices`, `Activate`, `Resolve`, `Run` / `RunEither`.

### [Configuration](/docs/sdk-reference/configuration) (Effect)

The `AddTrax` entry point and every extension method on the builder types (`TraxBuilder`, `TraxBuilderWithEffects`, `TraxBuilderWithMediator`), `TraxEffectBuilder`, and `TraxEffectBuilderWithData` -- data providers, effect providers, and orchestration setup. The builder pattern enforces configuration ordering at compile time: Effects -> Mediator -> Scheduler. Data provider methods (`UsePostgres`, `UseInMemory`) return `TraxEffectBuilderWithData`, which unlocks data-dependent methods like `AddDataContextLogging` at compile time.

Includes: `UsePostgres`, `UseInMemory`, `AddJson`, `SaveTrainParameters`, `AddJunctionLogger`, `AddDataContextLogging`, `AddMediator`, `AddEffect`, `AddJunctionEffect`, `SetEffectLogLevel`.

### [Mediator API](/docs/sdk-reference/mediator-api) (Mediator)

The `ITrainBus` interface for dynamically dispatching trains by input type, plus shared train discovery and execution services.

Includes: `RunAsync`, `InitializeTrain`, `AddMediator`, `ITrainDiscoveryService`, `ITrainExecutionService`.

### [Scheduler API](/docs/sdk-reference/scheduler-api) (Scheduler)

Scheduler configuration (`AddScheduler` + `SchedulerConfigurationBuilder`) and the runtime `ITraxScheduler` interface for scheduling, managing, and monitoring recurring trains.

Includes: `AddScheduler`, `Schedule`, `ScheduleMany`, dependent scheduling, manifest management, scheduling helpers (`Every`, `Cron`, `ManifestOptions`).

### [Dashboard API](/docs/sdk-reference/dashboard-api) (Dashboard)

Setup and configuration for the Trax Blazor dashboard.

Includes: `AddTraxDashboard`, `UseTraxDashboard`, `DashboardOptions`.

### [GraphQL API](/docs/sdk-reference/graphql-api) (API)

GraphQL schema for Trax using HotChocolate. Exposes train discovery, execution (via `[TraxQuery]`/`[TraxMutation]` whitelist), scheduler operations, and read-only queries.

Includes: `AddTraxGraphQL`, `UseTraxGraphQL`, `[TraxQuery]`/`[TraxMutation]` attributes, queries, mutations.
