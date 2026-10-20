---
title: The GraphQL API
description: "What Trax.Api.GraphQL is: a GraphQL layer over train execution and the shared database, its queue and run modes, its named schema, and where security fits."
type: explanation
area: api
nav_order: 1
---

# The GraphQL API

> **Security.** Trax ships authentication (`Trax.Api.Auth.*`) and audit (`Trax.Api.GraphQL.Audit`) packages alongside this library. NO WARRANTY. See [Secure a GraphQL host](/docs/security/secure-a-graphql-host) before deploying to production.

`Trax.Api.GraphQL` puts a GraphQL endpoint in front of the trains a host registers. A train opts in
with [`[TraxQuery]` or `[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
and gets a typed field; an EF Core entity opts in with
[`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models) and gets a paginated, filterable,
sortable query. Subscriptions stream a train's runs to its callers. It is built on HotChocolate,
which a consumer's train and entity code never has to name.

It ships as two packages. `Trax.Api` holds the DTOs, the train authorization enforcer and the
health check; `Trax.Api.GraphQL` holds the schema and depends on it, so a host references only
`Trax.Api.GraphQL`. To stand one up, [Getting Started](/docs/getting-started#6-call-it-over-graphql)
adds it to a working server, and [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
lists what it registers and every builder option.

## A thin layer over shared state

The API is designed to run on a different machine from the scheduler. The two share a database:
the API writes work queue entries and manifest changes, and the scheduler polls them and
dispatches trains. The API host runs no polling services or background workers of its own.

```
┌─────────────────────────────┐    ┌─────────────────────────────┐
│         API Server          │    │      Scheduler Server       │
│                             │    │                             │
│  GraphQL endpoint           │    │  ManifestManagerPolling     │
│  ITrainBus (direct runs)    │    │  JobDispatcherPolling       │
│  ITraxScheduler (DB writes) │    │  JobRunner                  │
│  ITrainDiscoveryService     │    │                             │
│  ITrainExecutionService     │    │                             │
└──────────────┬──────────────┘    └──────────────┬──────────────┘
               │                                   │
               └──────────┬───────────────────────┘
                          │
                          ▼
               ┌──────────────────────┐
               │     PostgreSQL       │
               │                      │
               │  manifests           │
               │  work_queue          │
               │  metadata            │
               │  dead_letters        │
               └──────────────────────┘
```

So an API host needs the mediator (to discover trains and run them) and a data provider, and not
`AddScheduler()`. Two things change that. A host that exposes the `operations` namespace needs the
services `AddScheduler()` registers (`IOperationsService`, and for the mutations `ITraxScheduler`
and a job submitter), and refuses to start without them. A host that also serves the
[dashboard](/docs/dashboard) needs them for the same reason. A host may also call `AddScheduler()`
just to seed manifests at startup.

## Queue or run

A train mutation either hands the work to the scheduler or does it now:

| Mode | What happens | Suits |
|---|---|---|
| Queue | A work queue entry is written. The scheduler picks it up on its next poll and runs the train on its own machine. The mutation returns at once. | Heavy work, work that should run on scheduler infrastructure, work that can wait |
| Run | `ITrainBus.RunAsync` runs the train on the API machine, or on a remote endpoint with [`UseRemoteRun()`](/docs/sdk-reference/scheduler-api/use-remote-run). The mutation returns the train's output. | Light work whose caller needs the result now |

A train exposes one or both (`GraphQLOperation.Queue`, `GraphQLOperation.Run`). With `UseRemoteRun()`
the API machine needs only the train's contract, not its code. On a host whose store is in memory
nothing dispatches the work queue, so a queue is refused there.

## Security is declared per surface

The endpoint is closed by default in the sense that matters: every exposed train and query model
says whether it is gated or public, and a host where one does not is refused at startup. The
`operations` namespace, which reads execution inputs and drives the scheduler, is off unless a host
exposes it, and exposing it requires a gate. Authentication is pluggable (API keys, JWTs, OIDC),
and every scheme feeds the same principal.

- [Secure a GraphQL host](/docs/security/secure-a-graphql-host) puts the pieces together in one `Program.cs`.
- [Authorization rules](/docs/security/authorization-rules) lists what gates each surface.
- [Startup refusals](/docs/security/startup-refusals) lists what stops a host.

## A named schema

The Trax schema is registered on a named HotChocolate schema, `"trax"`, rather than the default
one. A host can call `AddGraphQLServer()` for a schema of its own and serve both at different
paths. The hardening defaults, the authorization checks and the socket origin check belong to the
`trax` schema and do not touch the other one.

## Health

[AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) adds an ASP.NET Core
health check over the same database, reporting queue depth, in-progress and recently failed runs
and dead letters. The `operations.health` query returns the same data over GraphQL.

## Samples

Every sample serves the API; these show it in different topologies (see
[Samples](/docs/samples) for all of them):

- [The Trax book](/docs/book): one shop that grows from one process into a hub and workers, with
  JWT auth, scheduling and the operations console.
- [Content Shield](/docs/samples/content-shield): `UseRemoteWorkers()` and `UseRemoteRun()` send
  queued work, runs and queries to an ephemeral runner over signed HTTP requests.

The samples keep trains in a shared library and make each executable a thin wrapper that picks
which capabilities to enable.

## See also

- [Call another GraphQL server](/docs/api/call-another-graphql-server), for the other direction.
- [Persisted operations](/docs/api/persisted-operations), to serve only documents the host knows.
- [Add the dashboard to a host](/docs/dashboard), for a monitoring UI over the same data.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run)
