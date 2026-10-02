---
layout: default
title: SDK Reference
description: "Index of the Trax SDK reference by category: trains and junctions, attributes, train methods, configuration, mediator, scheduler, GraphQL, auth and testing."
nav_order: 13
has_children: true
section: SDK Reference
---

# SDK Reference

Complete reference documentation for every user-facing method in Trax. Each page documents the method signature, all parameters, return type, and usage examples.

For conceptual explanations and tutorials, see [Core](/docs/core), [Effect](/docs/effect), and [Mediator](/docs/mediator).

## Categories

### [Trains and Junctions](/docs/sdk-reference/trains-and-junctions) (Core, Effect)

The base classes you derive from and the helpers that register them: `Train<TInput, TReturn>`, `ServiceTrain<TIn, TOut>`, `Junction<TIn, TOut>`, `EffectJunction<TIn, TOut>`, `IRoute`, `MonadTask`, `TrainException` and `TrainExceptionData`.

Includes: `Train`, `ServiceTrain`, `Junction`, `EffectJunction`, `IRoute`, `MonadTask`, `TrainException`, `AddScopedTraxRoute` / `AddTransientTraxRoute` / `AddSingletonTraxRoute`.

### [Attributes](/docs/sdk-reference/attributes) (Effect)

Every attribute Trax reads, in one place: authorization, sensitive data, remote execution, GraphQL exposure.

Includes: `[TraxAuthorize]`, `[TraxAllowAnonymous]`, `[TraxSensitive]`, `[TraxRemote]`, `[Inject]`, and links to `[TraxQuery]`, `[TraxMutation]`, `[TraxBroadcast]`, `[TraxQueryModel]`, `[TraxConcurrencyLimit]`.

### [Train Methods](/docs/sdk-reference/train-methods) (Core)

Methods available on `Train<TInput, TReturn>` for composing junctions. `Junctions()` is the only way to declare a chain.

Includes: `Junctions`, `Chain`, `ShortCircuit`, `Extract`, `AddServices`, `Resolve`, `Run` / `RunEither`.

### [Configuration](/docs/sdk-reference/configuration) (Effect)

The `AddTrax` entry point and every extension method on the builder types (`TraxBuilder`, `TraxBuilderWithEffects`, `TraxBuilderWithMediator`), `TraxEffectBuilder`, and `TraxEffectBuilderWithData` -- data providers, effect providers, and orchestration setup. The builder pattern enforces configuration ordering at compile time: Effects -> Mediator -> Scheduler. Data provider methods (`UsePostgres`, `UseInMemory`) return `TraxEffectBuilderWithData`, which unlocks data-dependent methods like `AddDataContextLogging` at compile time.

Includes: `UsePostgres`, `UseInMemory`, `AddJson`, `SaveTrainParameters`, `AddJunctionLogger`, `AddDataContextLogging`, `AddMediator`, `AddEffect`, `AddJunctionEffect`, `SetEffectLogLevel`, `IDataContext`, `DomainDataContext`, `IJunctionEffectProvider`, `ITraxTrainEventClient`.

### [Mediator API](/docs/sdk-reference/mediator-api) (Mediator)

The `ITrainBus` interface for dynamically dispatching trains by input type, plus shared train discovery and execution services.

Includes: `RunAsync`, `InitializeTrain`, `AddMediator`, `AddServiceTrainBus`, `ITrainDiscoveryService`, `ITrainExecutionService`, `ITrustedExecutionScope`, `ICurrentPrincipalProvider`, `IConcurrencyLimiter`, `MediatorConfiguration`.

### [Scheduler API](/docs/sdk-reference/scheduler-api) (Scheduler)

Scheduler configuration (`AddScheduler` + `SchedulerConfigurationBuilder`) and the runtime `ITraxScheduler` interface for scheduling, managing, and monitoring recurring trains.

Includes: `AddScheduler`, `ITraxScheduler`, `Schedule`, `ScheduleMany`, dependent scheduling, manifest management, dead letters, scheduling helpers (`Every`, `Cron`, `ManifestOptions`).

### [State Machine API](/docs/sdk-reference/statemachine-api) (Effect)

Authoring portable snapshot state machines and hosting them: one-line discovery and wiring, plus the fluent builder for declaring states, transitions, guards, reducers, committed states, and exactly-once effects.

Includes: `AddStateMachines`, `Machine<TState, TTrigger>`, `IMachineBuilder`, `StateMachineMutations`, `AddTraxStateMachineSelfCheck`.

### [Dashboard API](/docs/sdk-reference/dashboard-api) (Dashboard)

Setup and configuration for the Trax Blazor dashboard.

Includes: `AddTraxDashboard`, `UseTraxDashboard`, `DashboardOptions`, `IDashboardSettingsService`.

### [GraphQL API](/docs/sdk-reference/graphql-api) (API)

GraphQL schema for Trax using HotChocolate. Exposes train discovery, execution (via `[TraxQuery]`/`[TraxMutation]` whitelist), scheduler operations, and read-only queries.

Includes: `AddTraxGraphQL`, `UseTraxGraphQL`, `[TraxQuery]`/`[TraxMutation]` attributes, queries, mutations, `AddTraxHealthCheck`.

### [GraphQL Client](/docs/sdk-reference/graphql-client) (API)

The outbound client: hand-written queries validated against the server's schema before they are sent, with keyed registrations for talking to several servers.

Includes: `AddTraxGraphQLClient`, `AddKeyedTraxGraphQLClient`, `TraxGraphQLClientBuilder`, `ValidateGraphQLClientAssembliesAsync`, `IGraphQLClientRequest`, `GraphQLResourceRequest`, `ResponseStrictness`.

### [API Auth](/docs/sdk-reference/api-auth) (API)

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible. See [API Security](/docs/api-security).

API-key authentication and the shared principal abstraction. Designed so future JWT/OIDC schemes drop in additively.

Includes: `AddTraxApiKeyAuth`, `TraxPrincipal`, `ITraxPrincipalResolver`, `ApiKeyDefaults`, `ApiKeyAuthenticationOptions`, `TraxAuthClaimTypes`.

### [API Audit](/docs/sdk-reference/api-audit) (API)

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible. See [API Security](/docs/api-security).

Request-level audit pipeline for Trax GraphQL hosts. Bounded channel, background batch writer, pluggable sink and redactor.

Includes: `AddAudit`, `TraxAuditEntry`, `ITraxAuditSink`, `ITraxAuditRedactor`, `TraxAuditOptions`.

### [Testing](/docs/sdk-reference/testing)

The test-support packages for your own test projects.

Includes: `Trax.Core.Testing` (`HygieneGuardFixture`, `RepoConventionGuardFixture`, `VocabularyGuards`, `ForeignVocabulary`, `ArchitectureGuardOptions`, `GuardResult`).
