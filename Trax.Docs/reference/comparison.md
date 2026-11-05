---
title: Trax compared
description: "Where Trax sits next to Temporal, Durable Functions, Elsa, MassTransit, XState, Stateless, Quartz.NET and Hangfire."
type: explanation
area: general
nav_order: 2
---

# Trax compared

Trax overlaps with several kinds of tool, and a different one is the right comparison depending on what you came for.
Each page below was checked against the other product's own docs and against Trax's code on the date it gives, and
each ends with when to choose the other product.

| If you are looking at | Read | The short version |
|---|---|---|
| Durable execution: Temporal, Azure Durable Functions | [Temporal and Durable Functions](/docs/reference/comparison/temporal-and-durable-functions) | They replay your workflow code so every `await` survives a crash. Trax records every run and makes the points you declare durable, with no determinism rules and no cluster to run, and its scheduler adds dependent schedules, group caps and dead letters. |
| .NET workflow engines and sagas: Elsa, MassTransit | [Elsa and MassTransit](/docs/reference/comparison/elsa-and-masstransit) | Elsa has a visual designer and runtime-editable workflows; MassTransit drives sagas from a message broker. Trax machines are driven by callers through a typed API, with a rule on every event. |
| State machine libraries: XState, Stateless | [XState and Stateless](/docs/reference/comparison/xstate-and-stateless) | Both are richer statecharts and leave storage, validation and authorization to you. Trax machines are flat, and the server stores them and applies every move. |
| Job schedulers: Quartz.NET, Hangfire | [Quartz.NET and Hangfire](/docs/reference/comparison/quartz-and-hangfire) | Both are simpler and stand alone, and both handle local time zones. Trax schedules trains with recorded runs, dependent schedules and dead letters, in UTC. |

## What Trax is, for the comparison

Trax is a set of NuGet packages for an ASP.NET Core app on .NET 10, with PostgreSQL in production. It has two halves
that work together:

- **Trains** are units of work written as a chain of typed steps. Every run is recorded in your database. You can run a
  train from code, serve it over GraphQL, schedule it, or send it to a worker.
- **State machines** hold a process that waits: for a person, an agent, a timer or a train. A client sends an event;
  the server checks it against the caller, the input and the stored state, and applies it or refuses it with a reason.

Trax is MIT licensed, with no commercial edition. [Versioning and stability](/docs/reference/versioning) says what a
release may change.
