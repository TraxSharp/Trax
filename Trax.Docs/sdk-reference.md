---
title: SDK Reference
description: "Index of the Trax SDK reference by package family: trains, attributes, configuration, mediator, scheduler, GraphQL, auth, dashboard, machines and testing."
type: landing
area: general
section: SDK Reference
has_children: true
nav_order: 1
---

# SDK Reference

Every public type and method a Trax package ships, one per page. Each page names its package, namespace and source files. For how the parts work, see [Core](/docs/core), [Effect](/docs/effect) and [Mediator](/docs/mediator).

## Core and Effect

| Section | Covers |
|---------|--------|
| [Trains and Junctions](/docs/sdk-reference/trains-and-junctions) | `Train`, `ServiceTrain`, `Junction`, `EffectJunction`, `IRoute`, `MonadTask`, `Either`, `TrainException` and the registration helpers |
| [Train Methods](/docs/sdk-reference/train-methods) | The members that declare, run and read a chain: `Junctions`, `Chain`, the decision steps, `Run`, `RunEither`, `DeclaredChain` |
| [Attributes](/docs/sdk-reference/attributes) | Every attribute Trax reads: `[TraxAuthorize]`, `[TraxAllowAnonymous]`, `[TraxSensitive]`, `[TraxRemote]`, `[Inject]` and the GraphQL exposure attributes |
| [Configuration](/docs/sdk-reference/configuration) | `AddTrax`, `AddEffects`, data providers, effect providers, junction effects, lifecycle hooks and broadcasters |
| [State Machine API](/docs/sdk-reference/statemachine-api) | Authoring, hosting and persisting snapshot state machines: `AddStateMachines`, `Machine<TState, TTrigger>`, `IMachineBuilder`, `StateMachineMutations`, and the TypeScript package |

## Mediator and Scheduler

| Section | Covers |
|---------|--------|
| [Mediator API](/docs/sdk-reference/mediator-api) | `ITrainBus`, train discovery and execution, concurrency limits, trusted execution scopes |
| [Scheduler API](/docs/sdk-reference/scheduler-api) | `AddScheduler`, `ITraxScheduler`, manifests, dead letters, remote and Lambda workers, scheduling helpers |

## Api

| Section | Covers |
|---------|--------|
| [GraphQL API](/docs/sdk-reference/graphql-api) | `AddTraxGraphQL`, the queries, mutations and subscriptions, query models and error codes |
| [GraphQL Client](/docs/sdk-reference/graphql-client) | The outbound client, with queries validated against the server's schema |
| [Persisted Operations](/docs/sdk-reference/persisted-operations) | Registering, enforcing and managing persisted GraphQL operations |
| [API Auth](/docs/sdk-reference/api-auth) | The principal abstraction and the API-key, JWT, Cognito and OIDC schemes |
| [API Audit](/docs/sdk-reference/api-audit) | The request-level audit pipeline: entries, sinks, redactors, options |

## Dashboard

| Section | Covers |
|---------|--------|
| [Dashboard API](/docs/sdk-reference/dashboard-api) | `AddTraxDashboard`, `UseTraxDashboard`, options and the settings service |
| [API Dashboard](/docs/sdk-reference/api-dashboard) | `UseTraxApiDashboard` and `TraxApiDashboardOptions`: the React operations dashboard a Trax.Api host serves over its GraphQL endpoint |

## Testing

| Section | Covers |
|---------|--------|
| [Testing](/docs/sdk-reference/testing) | The test-support packages: architecture guards, database invariants, machine conformance |
