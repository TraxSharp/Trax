---
layout: default
title: Home
description: "Trax .NET documentation home: what a train and its junctions are, the vocabulary, how the packages layer so you use only what you need, and where to go next."
nav_order: 1
---

# Trax .NET

Trax .NET is a framework for .NET business logic you can call, schedule, serve as an API, or move to workers, with every run recorded in your Postgres.

You write a train: a typed pipeline of small steps called junctions, where a failing junction skips the rest and the train returns the exception. Call it from a controller, put it on a cron schedule, publish it as a GraphQL mutation, or send it to a worker on another machine or in AWS Lambda. It is the same class each time, and every run leaves a record: when it started, how it ended, which junction failed and the exception it threw. Trax is MIT licensed and targets .NET 10.

New here? Start with the [Getting Started](/docs/getting-started) guide.

## The vocabulary

The type names in the API follow a train metaphor:

| Term | What it means |
|------|---------------|
| **Train** | A pipeline that follows a route, always stays on the tracks, always reaches a destination |
| **Route** | The path a train follows: a sequence of junctions (`IRoute<TIn, TOut>`) |
| **Junction** | A point on the route where work happens (`Junction<TIn, TOut>`). The train either continues right (success) or switches left (failure) |
| **Right track** | Success path. The train continues to the next junction |
| **Left track** | Failure path. The train bypasses remaining junctions and arrives with the exception |
| **Memory** | The cargo the train carries between junctions, wired automatically by type |
| **ServiceTrain** | A Train with equipment bolted on: execution tracking, logging, lifecycle management |

A train never leaves the rails. It always reaches a destination, either the intended output (right) or an exception (left). You'll see these terms throughout the docs and the API surface. The [Glossary](/docs/reference/glossary) defines the rest: Effect, Metadata, Manifest, the work queue, dead letters and the other names the docs use.

## Use only what you need

Each package adds one layer of capability. Stop at whatever layer solves your problem.

| Layer | What it adds |
|-------|-------------|
| [**Core**](/docs/core) | Typed pipelines, error propagation, chains declared in `Junctions()` that can be checked before they run |
| [**Effect**](/docs/effect) | Execution metadata, dependency injection, pluggable effect providers |
| [**Mediator**](/docs/mediator) | Decoupled dispatch: route by input type instead of direct injection, and the startup check that refuses to start a host whose chains cannot run |
| [**Scheduling**](/docs/scheduler) | Cron/interval scheduling, retries, dead-letter handling, job dependencies |
| [**API**](/docs/api) | Auto-generated GraphQL via HotChocolate |
| [**Dashboard**](/docs/dashboard) | Blazor monitoring UI that mounts into your existing app |

## Where to go

- [**Getting Started**](/docs/getting-started): hands-on code from Core-only to full stack
- [**Packages**](/docs/reference/packages): every package, what it is for, and which ones to install
- [**Samples & Deployment**](/docs/samples): project structure and deployment topologies
- [**SDK Reference**](/docs/sdk-reference): method-level documentation for every public API
