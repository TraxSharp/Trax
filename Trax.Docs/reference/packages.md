---
layout: default
title: Packages
description: "Every Trax NuGet package, grouped by layer: what it is for, the namespace to import, the methods it adds and which packages it needs beside it."
parent: Reference
nav_order: 3
---

# Packages

Trax ships as 42 NuGet packages from eight repositories. Most applications install four or five of them. This page lists every one: what it is for, the namespace you import to use it, the methods it adds, and what it needs beside it.

The pages describe the code on each repository's `main` branch. Each package versions on its own, so the docs do not name a version; the nuget.org link shows the latest release and its dependencies.

## Which packages you need

Each layer depends on the one before it, and installing a package brings the layers below it with it. Pick the highest layer you need, then add one data provider.

| You want | Install |
|---|---|
| Typed pipelines only, no DI, no database | `Trax.Core` |
| Trains recorded in a database | `Trax.Effect` and one of `Trax.Effect.Data.Postgres`, `Trax.Effect.Data.Sqlite`, `Trax.Effect.Data.InMemory` |
| Dispatch by input type, startup chain checks | add `Trax.Mediator` |
| Cron and interval schedules, retries, dead letters | add `Trax.Scheduler` (needs a data provider; Postgres when several servers share the work) |
| A GraphQL API over your trains | add `Trax.Api.GraphQL`, plus an auth package such as `Trax.Api.Auth.ApiKey` or `Trax.Api.Auth.Jwt` |
| The monitoring UI | add `Trax.Dashboard` |

A typical API host with a scheduler and the dashboard:

```bash
dotnet add package Trax.Effect.Data.Postgres
dotnet add package Trax.Scheduler
dotnet add package Trax.Api.GraphQL
dotnet add package Trax.Api.Auth.ApiKey
dotnet add package Trax.Dashboard
```

`Trax.Effect.Data.Postgres` brings `Trax.Effect.Data`, `Trax.Effect` and `Trax.Core`; `Trax.Scheduler` brings `Trax.Mediator`. The `using` lines each method needs are in the tables below.

## Trax.Core

| Package | What it is for | Import | Pairs with |
|---|---|---|---|
| [Trax.Core](https://www.nuget.org/packages/Trax.Core) | Trains, junctions and chains: `Train<TIn, TOut>`, `Junction<TIn, TOut>`, `Chain`, `ShortCircuit`, `Resolve`, Memory. No DI container or database needed. See [Core](/docs/core). | `Trax.Core.Train`, `Trax.Core.Junction`; `Either` and `Unit` come from `LanguageExt` | Nothing. Every other Trax package builds on it. |
| [Trax.Core.Testing](https://www.nuget.org/packages/Trax.Core.Testing) | Architecture-guard infrastructure and hygiene checkers: `HygieneGuards`, `RepoConventionGuards`, `VocabularyGuards`, `GuardResult`. See [Architecture Guards](/docs/reference/architecture-guards). | `Trax.Core.Testing`, `Trax.Core.Testing.Guards`, `Trax.Core.Testing.Fixtures` | A test project. The other `*.Testing` guard packages build on it. |
| [Trax.Core.Analyzers](https://www.nuget.org/packages/Trax.Core.Analyzers) | Deprecated. The Roslyn analyzer for CHAIN001 and CHAIN002 no longer reports anything; the host checks every chain at startup instead. Do not install it. See [Analyzer](/docs/core/analyzer). | | |

## Trax.Effect

| Package | What it is for | Import | Pairs with |
|---|---|---|---|
| [Trax.Effect](https://www.nuget.org/packages/Trax.Effect) | `ServiceTrain<TIn, TOut>`, which records every run as a Metadata row; DI registration with `AddTrax`, `AddEffects`, `AddScopedTraxRoute` and the other lifetimes; `AddLifecycleHook`, `UseBroadcaster`, `SetEffectLogLevel`; the `[TraxAuthorize]` and `[TraxAllowAnonymous]` attributes. See [Effect](/docs/effect). | `Trax.Effect.Extensions` for the registration methods, `Trax.Effect.Services.ServiceTrain` for the base class, `Trax.Effect.Attributes` for the attributes | One data provider below, to persist the runs. |
| [Trax.Effect.Data](https://www.nuget.org/packages/Trax.Effect.Data) | The shared data layer behind the three providers: `IDataContext` over the Trax tables, `AddDataContextLogging`, and `DomainDataContext` with `AddDomainDataContext` for your own schemas. See [Domain Data Contexts](/docs/effect/effect-providers/domain-data-contexts). | `Trax.Effect.Data.Extensions`, `Trax.Effect.Data.Services.DomainContext` | Comes with each provider; install it directly only for a library that defines a `DomainDataContext`. |
| [Trax.Effect.Data.Postgres](https://www.nuget.org/packages/Trax.Effect.Data.Postgres) | `UsePostgres(connectionString)` on the `AddEffects` builder, and `SkipMigrations()`. Persists runs to Postgres and applies the Trax schema migrations on startup. The production choice. See [Data Persistence](/docs/effect/effect-providers/data-persistence). | `Trax.Effect.Data.Postgres.Extensions` | `Trax.Effect`. Required by the scheduler when several servers share the work. |
| [Trax.Effect.Data.Sqlite](https://www.nuget.org/packages/Trax.Effect.Data.Sqlite) | `UseSqlite(connectionString)` on the `AddEffects` builder. For a single server or local development. See [UseSqlite](/docs/sdk-reference/configuration/use-sqlite). | `Trax.Effect.Data.Sqlite.Extensions` | `Trax.Effect`. |
| [Trax.Effect.Data.InMemory](https://www.nuget.org/packages/Trax.Effect.Data.InMemory) | `UseInMemory()` on the `AddEffects` builder. Runs are lost when the process exits. For tests and prototypes. See [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect). | `Trax.Effect.Data.InMemory.Extensions` | `Trax.Effect`. |
| [Trax.Effect.Data.Testing](https://www.nuget.org/packages/Trax.Effect.Data.Testing) | Data-layer architecture guards: `DataLayerGuards` and `DomainDataLayerGuardFixture` (one schema per context, contexts derive `DomainDataContext`, owner-scope filters). See [Architecture Guards](/docs/reference/architecture-guards). | `Trax.Effect.Data.Testing` | `Trax.Core.Testing`, in a test project. |
| [Trax.Effect.Decisions.SystemOne](https://www.nuget.org/packages/Trax.Effect.Decisions.SystemOne) | `AddNimbleDecider(...)`: answers a train's decisions with Bespoke Labs' open-weights Nimble, on a Nimble server you run. `AddSystemOneDecider(...)` does the same for Jev or another server that accepts the System One format. See [Decision Recording and Models](/docs/effect/decisions). | `Trax.Effect.Decisions.SystemOne.Extensions` | `Trax.Effect`. |
| [Trax.Effect.JunctionProvider.Logging](https://www.nuget.org/packages/Trax.Effect.JunctionProvider.Logging) | `AddJunctionLogger(serializeJunctionData)`: logs each junction's start, finish and duration. See [Junction Logger](/docs/effect/effect-providers/junction-logger). | `Trax.Effect.JunctionProvider.Logging.Extensions` | `Trax.Effect`. Junctions must derive `EffectJunction`. |
| [Trax.Effect.JunctionProvider.Progress](https://www.nuget.org/packages/Trax.Effect.JunctionProvider.Progress) | `AddJunctionProgress()`: writes the current junction to the Metadata row and checks for cancellation before each junction. See [Junction Progress](/docs/effect/effect-providers/junction-progress). | `Trax.Effect.JunctionProvider.Progress.Extensions` | `Trax.Effect` and a data provider, without which the host refuses to build. |
| [Trax.Effect.Provider.Json](https://www.nuget.org/packages/Trax.Effect.Provider.Json) | `AddJson()`: logs each tracked model's state as JSON while a train runs. For debugging. See [JSON Effect](/docs/effect/effect-providers/json-effect). | `Trax.Effect.Provider.Json.Extensions` | `Trax.Effect`. |
| [Trax.Effect.Provider.Parameter](https://www.nuget.org/packages/Trax.Effect.Provider.Parameter) | `SaveTrainParameters()`: serializes each train's input and output into its Metadata row, masking members marked `[TraxSensitive]`. See [Parameter Effect](/docs/effect/effect-providers/parameter-effect). | `Trax.Effect.Provider.Parameter.Extensions` | `Trax.Effect` and a data provider. |
| [Trax.Effect.Broadcaster.RabbitMQ](https://www.nuget.org/packages/Trax.Effect.Broadcaster.RabbitMQ) | `UseRabbitMq(connectionString)` on the `UseBroadcaster` builder, so lifecycle events raised in one process reach handlers in every other. See [Broadcaster Sinks](/docs/effect/broadcaster-sinks). | `Trax.Effect.Broadcaster.RabbitMQ.Extensions` | `Trax.Effect`. |
| [Trax.Effect.Broadcaster.SignalR](https://www.nuget.org/packages/Trax.Effect.Broadcaster.SignalR) | `UseSignalRHub()` on the `UseBroadcaster` builder and `MapTraxTrainEventHub()` for the endpoint, so browser and Blazor clients see train events as they happen. See [UseSignalRHub](/docs/sdk-reference/configuration/use-signalr-hub). | `Trax.Effect.Broadcaster.SignalR.Extensions` | `Trax.Effect`. |
| [Trax.Effect.StateMachine](https://www.nuget.org/packages/Trax.Effect.StateMachine) | The snapshot state-machine engine: state as language-neutral JSON that a C# backend and a TypeScript client agree on, authored through `IMachineBuilder`. No dependencies. See [State Machines](/docs/statemachine). | `Trax.Effect.StateMachine` | Usually `Trax.Effect.StateMachine.Persistence`. |
| [Trax.Effect.StateMachine.Persistence](https://www.nuget.org/packages/Trax.Effect.StateMachine.Persistence) | `Machine<TState, TTrigger>`, the user-scoped snapshot store, the draft service, the exactly-once effect primitive, `AddStateMachines` and `AddTraxStateMachineSelfCheck`. See [AddStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines). | `Trax.Effect.StateMachine.Persistence` | `Trax.Effect` and a data provider; `AddStateMachines` refuses to register without one. |
| [Trax.Effect.StateMachine.Testing](https://www.nuget.org/packages/Trax.Effect.StateMachine.Testing) | Replays the differential corpus produced by the TypeScript engine through the C# engine and returns the cases it fails to reproduce, proving the two engines agree. See [Codegen Pipeline](/docs/statemachine/codegen-pipeline). | `Trax.Effect.StateMachine.Testing` | A test project, and the corpus `trax machine generate` writes. |

## Trax.Mediator

| Package | What it is for | Import | Pairs with |
|---|---|---|---|
| [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator) | `AddMediator(...)` on the `AddTrax` builder: discovers trains in the assemblies you name, dispatches by input type through `ITrainBus`, and refuses to start a host whose chains cannot run. See [Mediator](/docs/mediator). | `Trax.Mediator.Extensions` for `AddMediator`, `Trax.Mediator.Services.TrainBus` for `ITrainBus` | `Trax.Effect`, and a data provider to record the runs. |
| [Trax.Mediator.Testing](https://www.nuget.org/packages/Trax.Mediator.Testing) | The `EveryTrainHasInterface` guard, through `TrainGuards` and `TrainGuardFixture`. See [Architecture Guards](/docs/reference/architecture-guards). | `Trax.Mediator.Testing` | `Trax.Core.Testing`, in a test project. |

## Trax.Scheduler

| Package | What it is for | Import | Pairs with |
|---|---|---|---|
| [Trax.Scheduler](https://www.nuget.org/packages/Trax.Scheduler) | `AddScheduler(...)` on the `AddTrax` builder: cron and interval manifests, the work queue, retries, dead letters and dependent trains. Also `ITraxScheduler` for scheduling at run time, `AddTraxWorker`, `AddTraxJobRunner` with `UseTraxRunEndpoint` for remote runners, and `AddTraxSchedulerLiveness`. See [Scheduling](/docs/scheduler). | `Trax.Scheduler.Extensions` for the registration methods, `Trax.Scheduler.Services.Scheduling` for `Every`, `Cron` and `Schedule`, `Trax.Scheduler.Services.TraxScheduler` for `ITraxScheduler` | `Trax.Mediator` (brought with it) and a data provider. With Postgres or SQLite it runs jobs on local workers; with `UseInMemory()` it runs them inline. Use Postgres when several servers share the work. |
| [Trax.Scheduler.Lambda](https://www.nuget.org/packages/Trax.Scheduler.Lambda) | `UseLambdaWorkers()` and `UseLambdaRun()`: dispatch queued jobs and synchronous runs to an AWS Lambda function by direct invocation. See [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers). | `Trax.Scheduler.Lambda.Extensions` | `Trax.Scheduler` in the scheduler host, and `Trax.Runner.Lambda` in the function. |
| [Trax.Scheduler.Sqs](https://www.nuget.org/packages/Trax.Scheduler.Sqs) | `UseSqsWorkers()`: dispatches queued jobs as SQS messages, plus the SQS-triggered Lambda consumer that runs them. See [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers). | `Trax.Scheduler.Sqs.Extensions`, `Trax.Scheduler.Sqs.Lambda` | `Trax.Scheduler`. |
| [Trax.Runner.Lambda](https://www.nuget.org/packages/Trax.Runner.Lambda) | `TraxLambdaFunction`, the base class for an AWS Lambda function that runs Trax trains. See [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function). | `Trax.Runner.Lambda` | `Trax.Scheduler.Lambda` in the host that invokes it. |

## Trax.Api

| Package | What it is for | Import | Pairs with |
|---|---|---|---|
| [Trax.Api](https://www.nuget.org/packages/Trax.Api) | The services behind the GraphQL API: `AddTraxApi` (which `AddTraxGraphQL` calls for you) and `AddTraxHealthCheck`. See [API](/docs/api). | `Trax.Api.Extensions` | Comes with `Trax.Api.GraphQL`. |
| [Trax.Api.GraphQL](https://www.nuget.org/packages/Trax.Api.GraphQL) | `AddTraxGraphQL(...)` and `UseTraxGraphQL()`: a HotChocolate schema with queries, mutations and subscriptions generated from your trains. See [API](/docs/api) and [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql). | `Trax.Api.GraphQL.Extensions` | A data provider and an auth package below. It brings `Trax.Api`, `Trax.Mediator` and `Trax.Scheduler` with it. |
| [Trax.Api.GraphQL.Audit](https://www.nuget.org/packages/Trax.Api.GraphQL.Audit) | `AddAudit<TSink>()` on the GraphQL builder: an audit entry for each operation, written to your sink. See [AddAudit](/docs/sdk-reference/api-audit/add-audit). | `Trax.Api.GraphQL.Audit` | `Trax.Api.GraphQL`. |
| [Trax.Api.GraphQL.PersistedOperations](https://www.nuget.org/packages/Trax.Api.GraphQL.PersistedOperations) | `UsePersistedOperations(...)` on the GraphQL builder and `AddPersistedOperationStore`: an allow-list of operations that callers run by id. See [Persisted Operations](/docs/persisted-operations). | `Trax.Api.GraphQL.PersistedOperations.Extensions` | `Trax.Api.GraphQL`. |
| [Trax.Api.GraphQL.Testing](https://www.nuget.org/packages/Trax.Api.GraphQL.Testing) | Cross-schema GraphQL guards through `CrossSchemaGuards` and `CrossSchemaGuardFixture`. See [Architecture Guards](/docs/reference/architecture-guards). | `Trax.Api.GraphQL.Testing` | `Trax.Core.Testing`, in a test project. |
| [Trax.Api.GraphQL.Client](https://www.nuget.org/packages/Trax.Api.GraphQL.Client) | A typed client for a Trax GraphQL API: `AddTraxGraphQLClient(uri)`, `AddKeyedTraxGraphQLClient`, and `ValidateGraphQLClientAssembliesAsync` to check your request types against the schema. See [GraphQL Client](/docs/api-graphql-client). | `Trax.Api.GraphQL.Client` | Any .NET caller; it needs no other Trax package. |
| [Trax.Api.GraphQL.Client.Typed](https://www.nuget.org/packages/Trax.Api.GraphQL.Client.Typed) | `TypedRequest<TResponse>`, which generates the query string from attributed request and result types. See [GraphQL Client](/docs/api-graphql-client). | `Trax.Api.GraphQL.Client.Typed` | `Trax.Api.GraphQL.Client`. |
| [Trax.Api.GraphQL.Client.Trax](https://www.nuget.org/packages/Trax.Api.GraphQL.Client.Trax) | `UseAssemblySchema(...)` and `UseStartupValidation(...)` on the client builder: validate against the server's schema built in-process. See [GraphQL Client](/docs/api-graphql-client). | `Trax.Api.GraphQL.Client.Trax` | `Trax.Api.GraphQL.Client`, in a .NET process that can reference the server's schema configuration. |
| [Trax.Api.Auth](https://www.nuget.org/packages/Trax.Api.Auth) | The principal model the auth packages share: `TraxPrincipal`, `ITraxPrincipalResolver`, `AddTraxPrincipalAccessor`. See [TraxPrincipal](/docs/sdk-reference/api-auth/trax-principal). | `Trax.Api.Auth` | Comes with each auth package below. |
| [Trax.Api.Auth.ApiKey](https://www.nuget.org/packages/Trax.Api.Auth.ApiKey) | `AddTraxApiKeyAuth(...)`: API-key authentication. See [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth). | `Trax.Api.Auth.ApiKey` | `Trax.Api.GraphQL`. |
| [Trax.Api.Auth.Jwt](https://www.nuget.org/packages/Trax.Api.Auth.Jwt) | `AddTraxJwtAuth(...)`, the `AddTraxCognitoJwtAuth`, `AddTraxEntraJwtAuth` and `AddTraxGoogleJwtAuth` presets, and `AddTraxJwtDispatcher` for several issuers on one endpoint. See [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth). | `Trax.Api.Auth.Jwt` | `Trax.Api.GraphQL`. |
| [Trax.Api.Auth.Jwt.Cognito](https://www.nuget.org/packages/Trax.Api.Auth.Jwt.Cognito) | `UseCognito(region, userPoolId, clientId)` on the JWT builder, which also checks Cognito's `token_use` claim. See [UseCognito](/docs/sdk-reference/api-auth/use-cognito). | `Trax.Api.Auth.Jwt.Cognito` | `Trax.Api.Auth.Jwt`. |
| [Trax.Api.Auth.Oidc](https://www.nuget.org/packages/Trax.Api.Auth.Oidc) | `AddTraxOidcAuth(...)`: the browser-facing OpenID Connect code flow, with a session cookie. See [AddTraxOidcAuth](/docs/sdk-reference/api-auth/add-trax-oidc-auth). | `Trax.Api.Auth.Oidc` | A host that browsers sign in to. Token-based API clients use `Trax.Api.Auth.Jwt` instead. |
| [Trax.Api.Auth.Jwt.Testing](https://www.nuget.org/packages/Trax.Api.Auth.Jwt.Testing) | `TestJwksServer` and `TestTokenIssuer`: a local JWKS endpoint and signed tokens for integration tests. See [JWT Testing](/docs/sdk-reference/api-auth/jwt-testing). | `Trax.Api.Auth.Jwt.Testing` | `Trax.Api.Auth.Jwt`, in a test project. |
| [Trax.Api.Auth.Jwt.Cognito.Issuer](https://www.nuget.org/packages/Trax.Api.Auth.Jwt.Cognito.Issuer) | `CreateCognitoIssuer()` on `TestJwksServer`: tokens shaped like Cognito's, for testing `UseCognito`. See [Cognito Issuer](/docs/sdk-reference/api-auth/cognito-issuer). | `Trax.Api.Auth.Jwt.Cognito.Issuer` | `Trax.Api.Auth.Jwt.Testing` and `Trax.Api.Auth.Jwt.Cognito`, in a test project. |

## Trax.Dashboard

| Package | What it is for | Import | Pairs with |
|---|---|---|---|
| [Trax.Dashboard](https://www.nuget.org/packages/Trax.Dashboard) | `AddTraxDashboard(...)` and `UseTraxDashboard()`: the Blazor Server monitoring UI, mounted at `/trax` in your app. See [Dashboard](/docs/dashboard). | `Trax.Dashboard.Extensions` | `Trax.Scheduler` and a data provider (it brings `Trax.Scheduler` with it). |

## Tools and templates

| Package | What it is for | Install | Pairs with |
|---|---|---|---|
| [Trax.Cli](https://www.nuget.org/packages/Trax.Cli) | The `trax` dotnet tool: `trax generate` scaffolds a project from a GraphQL SDL or OpenAPI schema, and `trax machine` scaffolds a state machine and generates its TypeScript twin. See [CLI](/docs/reference/cli). | `dotnet tool install --global Trax.Cli` | Nothing; it is a tool, not a library. |
| [Trax.Samples.Templates](https://www.nuget.org/packages/Trax.Samples.Templates) | `dotnet new` templates: `trax-api`, `trax-scheduler` and `trax-hub`. See [Templates](/docs/reference/templates). | `dotnet new install Trax.Samples.Templates` | Nothing; the generated projects reference the packages they need. |

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard)
