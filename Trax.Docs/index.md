---
title: Home
description: "Trax .NET documentation home: what Trax is, and where to go to learn it, get a task done, understand how it works, or look something up."
type: landing
area: general
nav_order: 1
---

# Trax .NET

Trax .NET is a framework for .NET business logic you can call, schedule, serve as an API, or move
to workers, with every run recorded in your Postgres. You write a train: a typed pipeline of small
steps called junctions, where a failing junction skips the rest and the train returns the
exception. The same class runs from a controller, on a cron schedule, as a GraphQL mutation, or on
a worker in another process or in AWS Lambda, and every run leaves a record of when it started, how
it ended, and which junction failed with what. Trax is MIT licensed and targets .NET 10. The
[Glossary](/docs/reference/glossary) defines the names the docs use.

## Where to go

[How these docs are organised](/docs/about-these-docs) explains the four kinds of page below.

### Learn

- [Getting Started](/docs/getting-started): build a server with one train, record its runs in
  Postgres, schedule it, open the dashboard and call it over GraphQL.

### Do

- [Set up the scheduler](/docs/scheduler/setup): install it, configure it, and schedule a train.
- [Run a job once, later](/docs/scheduler/delayed-jobs): delayed and one-off runs.
- [Handle dead letters](/docs/scheduler/dead-letters-and-cleanup): retry or acknowledge runs that
  ran out of retries.
- [Secure a GraphQL host](/docs/api-security): authentication, authorization and hardening, end to
  end.
- [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts): your own EF Core
  tables next to Trax's.
- [Debug with the log table](/docs/effect/debug-across-processes-with-the-log-table): follow a run across
  processes through `trax.log`.
- [Test your trains](/docs/cross-cutting/testing): unit and in-memory tests.
- [Author a state machine](/docs/statemachine/authoring): define one and serve it.

### Understand

- [Core](/docs/core): trains, junctions, and how a chain runs.
- [Effect](/docs/effect): what an effect is and how runs get recorded.
- [Mediator](/docs/mediator): running a train by its input type, and nested trains.
- [Scheduling](/docs/scheduler): manifests, the work queue, and how jobs are dispatched.
- [State Machines](/docs/statemachine): snapshots and definitions, the guarantees, and the matching
  C# and TypeScript runtimes.
- [Authorization](/docs/authorization): where Trax checks who may run what.
- [Deployment topologies](/docs/reference/deployment-topologies): the ways to split an application
  across processes, and the sample that runs each.
- [Trax compared with Quartz.NET and Hangfire](/docs/reference/comparison): what each is for.

### Look up

- [SDK Reference](/docs/sdk-reference): every public type and method.
- [Packages](/docs/reference/packages): every package, what it is for, and which to install.
- [Glossary](/docs/reference/glossary): the names Trax uses.
- [CLI](/docs/reference/cli): the `trax` command, which scaffolds a project from a GraphQL schema or
  OpenAPI spec.
- [Troubleshooting](/docs/cross-cutting/troubleshooting): startup and runtime errors, verbatim, with
  the fix.
- [Schema migrations](/docs/reference/schema-migrations): what each release changes in the database.

### Upgrade and explore

- [Upgrading](/docs/migration-guides): the migration guides, newest first.
- [Samples](/docs/samples): complete applications, each proving one feature end to end.
