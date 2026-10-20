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

Everything in Trax is a way to use a train. The database records its runs, the mediator, the
scheduler and workers decide when and where it runs, the GraphQL API and the operations console let
a client or an operator call it and watch it, and a state machine walks a user through a longer
process and runs trains along the way. [What Trax does](/docs/what-trax-does) maps every capability
to the chapter that teaches it and the page that explains it.

## Start here

New to Trax, read these in order. Everything else on this site is for when you need it.

1. [What Trax does](/docs/what-trax-does): every capability on one page, and how they fit.
2. [Quick start](/docs/getting-started): one app from an empty folder, in-process to GraphQL.
3. [Trains and junctions](/docs/core/trains-and-junctions): what a train is and how it runs.
4. [Memory](/docs/core/memory): how a junction gets its input.
5. [How a chain runs](/docs/core/building-chains): the steps a train can declare.
6. [Handle errors in trains](/docs/cross-cutting/handle-errors-in-trains): what a failing junction
   does to the rest.
7. [Test a train](/docs/cross-cutting/test-a-train): with in-memory effects, no database.
8. [Recording runs](/docs/effect): what gets recorded for each run, and where.
9. [Calling a train by its input](/docs/mediator): how every caller starts a train.
10. [Schedule a train](/docs/scheduler/setup): cron, intervals and one-off runs.
11. [The GraphQL API](/docs/api): serving trains to a client.
12. [Authorization rules](/docs/security/authorization-rules): nothing is served until a rule allows it.
13. [Operations console](/docs/dashboard): finding and requeuing runs.
14. [Train or machine](/docs/statemachine/train-or-machine): when a process needs a state machine.
15. [Troubleshooting](/docs/cross-cutting/troubleshooting): startup and runtime errors, verbatim.
16. [Packages](/docs/reference/packages): which ones to install.

## Where to go

[How these docs are organised](/docs/about-these-docs) explains the four kinds of page below.

### Learn

- [The Trax book](/docs/book): build one shop from an empty folder to a deployed hub, workers and a
  React storefront, one feature per chapter.
- [Getting Started](/docs/getting-started): build a server with one train, record its runs in
  Postgres, schedule it, open the operations console and call it over GraphQL.

### Do

- [Set up the scheduler](/docs/scheduler/setup): install it, configure it, and schedule a train.
- [Run a job once, later](/docs/scheduler/delayed-jobs): delayed and one-off runs.
- [Handle dead letters](/docs/scheduler/dead-letters-and-cleanup): retry or acknowledge runs that
  ran out of retries.
- [Secure a GraphQL host](/docs/security/secure-a-graphql-host): authentication, authorization and hardening, end to
  end.
- [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts): your own EF Core
  tables next to Trax's.
- [Debug with the log table](/docs/effect/debug-across-processes-with-the-log-table): follow a run across
  processes through `trax.log`.
- [Test your trains](/docs/cross-cutting/testing): unit and in-memory tests.
- [Author a state machine](/docs/statemachine/authoring): define one and serve it.

### Understand

- [What Trax does](/docs/what-trax-does): every capability, with where to learn and read about it.
- [Trains](/docs/core): trains, junctions, and how a chain runs.
- [Decisions](/docs/core/decisions): a branch in a chain as a typed question a rule or a model answers.
- [Checkpoints and branches](/docs/core/checkpoints-and-branches): steps side by side, and resuming a
  failed run.
- [Recording runs](/docs/effect): what an effect is and how runs get recorded.
- [Calling a train by its input](/docs/mediator): the train bus, queued work and nested trains.
- [Scheduling](/docs/scheduler): manifests, the work queue, and how jobs are dispatched.
- [How retries work](/docs/scheduler/how-retries-work): backoff, dead letters and requeues.
- [Choosing where trains run](/docs/scheduler/remote-execution): in the host, on worker
  processes, over SQS or in Lambda.
- [The GraphQL API](/docs/api): trains as queries and mutations, and query models.
- [Persisted operations](/docs/api/persisted-operations): a server that runs only the documents you
  registered.
- [Operations console](/docs/dashboard): what an operator sees and can do.
- [State Machines](/docs/statemachine): snapshots and definitions, the guarantees, and the matching
  C# and TypeScript runtimes.
- [Train or machine](/docs/statemachine/train-or-machine): which one a piece of work needs.
- [Authorization](/docs/security/authorization-rules): where Trax checks who may run what.
- [Architecture guards](/docs/reference/architecture-guards): tests that keep an application in
  the shape Trax expects.
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
