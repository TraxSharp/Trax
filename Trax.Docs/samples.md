---
title: Samples
description: "The book first, then the samples worth reading on their own: the ones you can watch run and the shapes the book does not build, with what they need to run."
type: landing
area: samples
nav_order: 1
---

# Samples

Start with [the Trax book](/docs/book). It builds one shop, Shopfront, from an empty folder to a deployed hub,
workers and a React storefront, and it covers most of what the samples show one feature at a time: scheduling,
retries and dead letters, JWT auth and `[TraxAuthorize]`, workers over RabbitMQ, state machines, persisted
operations and a generated client. The finished project is in
[Trax.Samples/book/Shopfront](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/book/Shopfront), held to the
book's pages in CI, with the book's tests run against it.

The pages below are the samples worth reading on their own: each shows something you can watch run, or a shape the
book does not build. Each has an end-to-end suite that runs it against a real host, and its code is quoted from the
sample and checked against it in CI.

## Watch it run

| Sample | What you see | Run (from `Trax.Samples/`) | Port |
|---|---|---|---|
| [Chat Service](/docs/samples/chat-service) | A chat app in React: each message is a train, and a lifecycle hook feeds an `onChatEvent` subscription only a room's participants may join | `dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api` | 5210 |
| [Recovery](/docs/samples/recovery) | A run that crashes and resumes on a live page: recorded decisions replayed instead of asking the model again, a checkpoint, parallel branches, state machines that run trains | `dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api` | 5260 |
| [Persisted Operations](/docs/samples/persisted-operations) | An API that runs only stored documents, on a page: gated management mutations, a hot-fix by id, the shape-diff guardrail | `dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api` | 5240 |

## Shapes the book does not build

| Sample | The problem it solves | Run (from `Trax.Samples/`) | Port |
|---|---|---|---|
| [Bookworm](/docs/samples/bookworm) | Two domains with a schema and context each, joined by a batched cross-schema GraphQL edge, with owner-scoped rows | `dotnet run --project samples/Bookworm/Trax.Samples.Bookworm.Api` | 5250 |
| [GraphQL Client](/docs/samples/graphql-client) | A .NET service calling other Trax servers through keyed clients, each request validated against its server's schema before it is sent | `dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient.Gateway` | 5310-5314 |
| [Content Shield](/docs/samples/content-shield) | An API that executes nothing: queued and synchronous runs go, signed, to a Lambda-style runner | runner and API, see the page | 5204-5205 |
| [SignalR Broadcaster](/docs/samples/signalr-broadcaster) | Live train events in a browser through SignalR rather than GraphQL, on a hub only signed-in operators may join | `dotnet run --project samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster` | 5270 |

The other samples ([Scheduling](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Scheduling), [Auth](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Auth),
[State Machine](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/StateMachine), [Energy Hub](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/DistributedWorkers)) are there mainly to prove
their feature end to end against a real host; each has a README with how to run it, and the book covers the same
ground. The [feature-coverage table](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples#feature-coverage)
lists every major feature and the test that proves it.

Starting a new server rather than learning? The [project templates](/docs/reference/templates)
(`trax-hub`, `trax-api`, `trax-scheduler`) scaffold a runnable host with a README and a test project:
`dotnet new install Trax.Samples.Templates && dotnet new trax-hub -n MyApp`.

The ports never collide, so any set of samples can run side by side. The React clients (Chat Service, Recovery,
State Machine) use Vite's dev server on 5173, the one origin their hosts allow; run one at a time. A second
client refuses to start rather than move to another port.

## Running the samples

Most samples need PostgreSQL, and the distributed ones RabbitMQ. From the `Trax.Samples/` directory:

```bash
docker compose up -d
```

The compose file publishes Postgres (`trax` / `trax123`) and RabbitMQ (`trax` / `trax123`) on
`127.0.0.1` only, because their passwords are written in the file. Set `TRAX_PG_PORT` to move Postgres to another
host port when something else holds 5432; the samples themselves read their connection string from their own
`appsettings.json`, so change it there too. If you copy the compose file to a server, keep the `127.0.0.1:` prefix and
replace the passwords.

Every Postgres sample has a database of its own (`trax_auth`, `trax_bookworm`, `trax_contentshield`, `trax_energyhub`,
`trax_persisted_operations`, `trax_recovery`, `trax_scheduling`, `trax_statemachine`), because a scheduler polls every
manifest and job in its database and would fail another sample's. `docker compose up -d` creates them on a fresh
volume. Its init script runs only once, so on a volume from before a database was added, create the database yourself:
`docker exec trax_database createdb -U trax trax_scheduling`.

The samples' demo API keys and JWT signing keys are published in this repository, so each sample registers them only
in Development, and so does its dashboard. `dotnet run` starts in Development through the project's
`Properties/launchSettings.json`; started any other way, a sample accepts none of them and serves no dashboard. Every
demo API key contains `do-not-use-in-production`, which Trax.Api refuses to start with outside Development.

The end-to-end suites run with `dotnet test` from `Trax.Samples/`. They use Postgres on port 5432 by default; set
`TRAX_TEST_PG_PORT` to point them elsewhere. A suite whose database is missing fails rather than skipping.

## Beyond one sample

How the samples' trains sit in a library that several executables share is in
[Structuring your application](/docs/reference/project-layout#trains-in-a-class-library). The five ways a Trax
application can be split across processes, with the sample that runs each, are in
[Deployment topologies](/docs/reference/deployment-topologies). The one-project, one-schema, one-context data layout
is the [Bookworm](/docs/samples/bookworm) sample's subject, and the meta-tests that hold a consumer to it are the
[architecture guards](/docs/reference/architecture-guards).
