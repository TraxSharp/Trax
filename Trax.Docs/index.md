---
layout: default
title: Home
nav_order: 1
---

# Trax

Trax is a .NET framework for structuring business logic as typed pipelines. Each pipeline is a sequence of junctions where every junction has one job, typed input and output, and errors short-circuit automatically. Your configuration lives in `Program.cs`, your logic lives in pipelines.

New here? Start with the [Getting Started](/docs/getting-started) guide.

## The vocabulary

The website calls them "pipelines." The code calls them "trains." Same thing, different name. The entire framework uses a train metaphor:

| Term | What it means |
|------|---------------|
| **Train** | A pipeline that follows a route, always stays on the tracks, always reaches a destination |
| **Route** | The path a train follows: a sequence of junctions (`IRoute<TIn, TOut>`) |
| **Junction** | A point on the route where work happens (`Junction<TIn, TOut>`). The train either continues right (success) or switches left (failure) |
| **Right track** | Success path. The train continues to the next junction |
| **Left track** | Failure path. The train bypasses remaining junctions and arrives with the exception |
| **Memory** | The cargo the train carries between junctions, wired automatically by type |
| **ServiceTrain** | A Train with equipment bolted on: execution tracking, logging, lifecycle management |

A train never leaves the rails. It always reaches a destination, either the intended output (right) or an exception (left). You'll see these terms throughout the docs and the API surface.

## Use only what you need

Each package adds one layer of capability. Stop at whatever layer solves your problem.

| Layer | What it adds |
|-------|-------------|
| [**Core**](/docs/core) | Typed pipelines, error propagation, compile-time analyzer |
| [**Effect**](/docs/effect) | Execution metadata, dependency injection, pluggable effect providers |
| [**Mediator**](/docs/mediator) | Decoupled dispatch: route by input type instead of direct injection |
| [**Scheduling**](/docs/scheduler) | Cron/interval scheduling, retries, dead-letter handling, job dependencies |
| [**API**](/docs/api) | Auto-generated GraphQL via HotChocolate |
| [**Dashboard**](/docs/dashboard) | Blazor monitoring UI that mounts into your existing app |

## Where to go

- [**Getting Started**](/docs/getting-started): hands-on code from Core-only to full stack
- [**Samples & Deployment**](/docs/samples): project structure and deployment topologies
- [**SDK Reference**](/docs/sdk-reference): method-level documentation for every public API
