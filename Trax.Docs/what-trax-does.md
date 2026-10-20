---
title: What Trax does
description: "A map of everything Trax does, one capability per row, with the book chapter that teaches it, the page that explains it and the package that ships it."
type: explanation
area: general
nav_order: 2
---

# What Trax does

Trax is built around one thing: the **train**, a class that does one piece of business work as a
chain of small steps called junctions. Everything else in Trax is a way to use that same class.
The database records every run of it. The mediator, the scheduler and workers decide when and
where it runs. The GraphQL API and the operations console let a client or an operator call it and
watch it. A state machine walks a user through a longer process and runs trains along the way. You
write the train once, and each of these attaches to it without changing it.

```text
                          ┌──────────────────────────────┐
  your code ─────────────►│                              │
  a GraphQL client ──────►│           a train            │───► a run record
  the scheduler ─────────►│  junction → junction → ...   │     (when, how it ended,
  a worker process ──────►│                              │      which step failed, why)
  a state machine ───────►│                              │            │
                          └──────────────────────────────┘            ▼
                                                            the operations console
```

The [Glossary](/docs/reference/glossary) gives each Trax name its plain equivalent. The tables
below list what Trax does, grouped the way [the book](/docs/book) teaches it. Each row names the
chapter that builds it in a real application, the page that explains it, and the package to
install ([Packages](/docs/reference/packages) has the full list).

## Write the work

| Capability | In plain words | Learn it | Understand it | Package |
|---|---|---|---|---|
| Trains and junctions | One operation as typed steps; a failing step stops the rest and the caller gets the exception. | [Chapter 1](/docs/book/an-order-is-a-train) | [Trains and junctions](/docs/core/trains-and-junctions) | `Trax.Core` |
| Memory | Each step's output is handed to later steps by its type, with no wiring code. | [1.6](/docs/book/an-order-is-a-train/the-train) | [Memory](/docs/core/memory) | `Trax.Core` |
| The startup chain check | A host whose chain can never run refuses to start, naming the step and the missing type. | [1.9](/docs/book/an-order-is-a-train/when-the-chain-does-not-fit) | [Startup chain check](/docs/core/startup-chain-check) | `Trax.Mediator` |
| Decisions | A branch in the chain is a typed question a decider answers: a rule, a lookup or a model. Answers can be recorded and replayed. | [Chapter 5](/docs/book/does-this-order-look-like-fraud) | [Decisions](/docs/core/decisions) | `Trax.Core` |
| Parallel steps and checkpoints | Steps that run side by side, and a saved point a failed run resumes after. | [Chapter 14](/docs/book/checks-in-parallel-and-resuming) | [Checkpoints and branches](/docs/core/checkpoints-and-branches) | `Trax.Core` |
| Testing | Test a step on its own, a train on in-memory storage, and the whole host as a client calls it. | [Chapter 2](/docs/book/testing-place-order) | [Testing](/docs/cross-cutting/testing) | `Trax.Effect.Data.InMemory` |

## Record every run

| Capability | In plain words | Learn it | Understand it | Package |
|---|---|---|---|---|
| The run record | Every run is a row: its input, output, timing, and the step and exception that failed it. | [Chapter 3](/docs/book/every-order-is-recorded) | [Trax.Effect](/docs/effect) | `Trax.Effect` and a data provider |
| Your own tables | Your application's EF Core data beside Trax's, in the same database. | [Chapter 4](/docs/book/the-shops-own-data) | [Domain data contexts](/docs/effect/effect-providers/domain-data-contexts) | `Trax.Effect.Data.Postgres` |
| Run events | Lifecycle hooks and junction events, pushed to other processes and to browsers. | [Chapter 10](/docs/book/failures-retries-and-dead-letters) | [How event broadcasting works](/docs/effect/how-event-broadcasting-works) | `Trax.Effect` |

## Run it when and where you need

| Capability | In plain words | Learn it | Understand it | Package |
|---|---|---|---|---|
| Calling a train by its input | Run a train by handing over its input, without referencing the class. | [Chapter 2](/docs/book/testing-place-order) | [Mediator](/docs/mediator) | `Trax.Mediator` |
| Queued work | Ask for a run now and let it start when there is room, capped per train. | [Chapter 11](/docs/book/a-separate-worker) | [Queued work](/docs/mediator/queued-work) | `Trax.Mediator` |
| Schedules | Cron, intervals, one-off delays, and trains that run after another succeeds. | [Chapter 9](/docs/book/scheduled-work) | [Scheduling](/docs/scheduler) | `Trax.Scheduler` |
| Retries and dead letters | Retry with backoff, then park the run for an operator to requeue or acknowledge. | [Chapter 10](/docs/book/failures-retries-and-dead-letters) | [How retries work](/docs/scheduler/how-retries-work) | `Trax.Scheduler` |
| Workers | Run the same trains in other processes, over HTTP, SQS or AWS Lambda, and recover a run whose worker died. | [Chapter 11](/docs/book/a-separate-worker) | [Deployment topologies](/docs/reference/deployment-topologies) | `Trax.Scheduler` |

## Serve it

| Capability | In plain words | Learn it | Understand it | Package |
|---|---|---|---|---|
| Authorization | Nothing is served until a rule says who may call it; an unguarded train stops the host. | [Chapter 6](/docs/book/customers-and-staff) | [Authorization rules](/docs/security/authorization-rules) | `Trax.Effect` (the attributes), `Trax.Api.Auth` (sign-in) |
| GraphQL | Trains become typed queries and mutations, and query models become filterable queries. | [Chapter 7](/docs/book/serve-it-over-graphql) | [The GraphQL API](/docs/api) | `Trax.Api.GraphQL` |
| The operations console | Every run, schedule, dead letter and machine in one place, with requeue and cancel. | [Chapter 8](/docs/book/the-operations-console) | [Operations console](/docs/dashboard) | `Trax.Api.Dashboard` |
| Generated clients | A typed TypeScript client generated from the documents your frontend writes. | [Chapter 15](/docs/book/a-generated-client) | [Trax CLI](/docs/reference/cli) | the `trax` tool |
| Persisted operations | The server runs only the GraphQL documents you registered and refuses the rest. | [Chapter 17](/docs/book/require-persisted-operations) | [Persisted operations](/docs/api/persisted-operations) | `Trax.Api.GraphQL.PersistedOperations` |

## Walk a user through a process

| Capability | In plain words | Learn it | Understand it | Package |
|---|---|---|---|---|
| State machines | A long-lived flow (a checkout, a form) as named states the server and the browser agree on. | [Chapter 12](/docs/book/checkout-is-a-state-machine) | [State machines](/docs/statemachine) | `Trax.Effect.StateMachine` |
| A machine runs a train | A state starts a train and moves on with its result. | [Chapter 13](/docs/book/the-machine-places-the-order) | [Train or machine](/docs/statemachine/train-or-machine) | `Trax.Effect.StateMachine` |
| The browser twin | The same machine in TypeScript, so the browser predicts each move before the server confirms it. | [Chapter 16](/docs/book/checkout-in-the-browser) | [State machines](/docs/statemachine) | `@traxsharp/state-machine` |

## Ship it

| Capability | In plain words | Learn it | Understand it | Package |
|---|---|---|---|---|
| Deployment | One host, or a hub and workers against one Postgres and RabbitMQ, with health checks. | [Chapter 18](/docs/book/deploy) | [Deployment topologies](/docs/reference/deployment-topologies) | |
| Architecture guards | Tests your build runs to keep trains, junctions and registrations in the shape Trax expects. | [Chapter 2](/docs/book/testing-place-order) | [Architecture guards](/docs/reference/architecture-guards) | `Trax.Core.Testing` |

## SDK Reference

> [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
