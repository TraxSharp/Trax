---
title: Home
description: "Trax .NET documentation home: what Trax is, and where to go to learn it, get a task done, understand how it works, or look something up."
type: landing
area: general
nav_order: 1
---

# Trax .NET

Trax makes sure a process happens in the right order, by the right people, with nothing skipped,
whether a person, a script or an AI agent is doing the work.

![A browser, a script and an AI agent each propose a move. The server reads the stored state and checks three things: is the move allowed from here, may this caller make it, do the data and evidence hold up. All three pass and the new state is saved; any one fails and the client gets a typed error such as GUARD_FAILED, and nothing changes.](/docs-images/propose-decide.svg)

You define the process once, in C#. Any client can drive it, and the server checks every move and
refuses a bad one with a typed error that says why. Every run is recorded in your own database. Trax
is MIT licensed and targets .NET 10.

**New here?** Read [Trax in five minutes](/docs/introduction): what Trax is for, in pictures, with
no code.

## Pick your way in

**Deciding whether Trax fits**

- [Trax in five minutes](/docs/introduction): the problem, the idea, and how the pieces fit.
- [What Trax does](/docs/what-trax-does): every capability on one page, and where each is taught.
- [Trax compared with Quartz.NET and Hangfire](/docs/reference/comparison): what each is for.
- [Samples](/docs/samples): complete applications you can run.

**Building with it in .NET**

1. [Quick start](/docs/getting-started): one app from an empty folder, in-process to GraphQL.
2. [Trains and junctions](/docs/core/trains-and-junctions): what a train is and how it runs.
3. [Test a train](/docs/cross-cutting/test-a-train): with in-memory effects, no database.
4. [The Trax book](/docs/book): a whole shop, one feature per chapter.

**Putting AI agents to work on it**

- [Board](/docs/samples/board): scripted agents work tickets, and the server checks their evidence.
- [Train or machine](/docs/statemachine/train-or-machine): which one a piece of work needs.
- [State machines](/docs/statemachine): states, moves and the guarantees the server keeps.
- [Authorization rules](/docs/security/authorization-rules): nothing is served until a rule allows it.

**Running it in production**

- [Operations console](/docs/dashboard): finding, reading and requeuing runs.
- [How retries work](/docs/scheduler/how-retries-work): backoff, dead letters and requeues.
- [Deployment topologies](/docs/reference/deployment-topologies): one host, or a hub and workers.
- [Troubleshooting](/docs/cross-cutting/troubleshooting): startup and runtime errors, verbatim.

The [Glossary](/docs/reference/glossary) gives every Trax name its plain meaning.

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

- [Trax in five minutes](/docs/introduction): what Trax is for, in pictures.
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
