---
title: Samples
description: "Every Trax sample, the feature each one proves, the command that runs it, and what the samples need to run: Postgres, RabbitMQ and their databases."
type: landing
area: samples
nav_order: 1
---

# Samples

[Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) holds one sample per major Trax feature. Each is a complete,
runnable application that shows that feature and as little else as possible, and each has an end-to-end test suite
that runs against the real host, so what a page here tells you to copy is something CI has seen work. Start from the
sample for the feature you need, then read its page: what it proves and the test that proves it, how to run it, a
"Try it" walkthrough whose commands were run as written, and the code that matters, quoted from the sample and
checked against it in CI.

Features without a sample of their own are proven by a test in their own package's test projects; the
[feature-coverage table](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples#feature-coverage) lists every major feature and
the test class that proves it.

Starting a new server rather than learning one feature? The [project templates](/docs/reference/templates)
(`trax-hub`, `trax-api`, `trax-scheduler`) scaffold a runnable host with a README and a test project:
`dotnet new install Trax.Samples.Templates && dotnet new trax-hub -n MyApp`.

## Pick a sample

| Sample | The feature it proves | Run (from `Trax.Samples/`) | Port |
|---|---|---|---|
| [Scheduling](/docs/samples/scheduling) | Interval, cron, one-off, dependent and dormant manifests; retries with backoff, dead letters and their requeue over GraphQL | `dotnet run --project samples/Scheduling/Trax.Samples.Scheduling.Host` | 5230 |
| [Auth](/docs/samples/auth) | Securing a Trax server end to end: API keys and JWT side by side, `[TraxAuthorize]` roles and policies, `GateOperations`, scheme-qualified principal ids, the audit trail | `dotnet run --project samples/Auth/Trax.Samples.Auth` | 5220 |
| [Recovery](/docs/samples/recovery) | Train decisions, a manifest retry that replays them instead of asking the model again, and live junction events on a React page | `dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api` | 5260 |
| [Chat Service](/docs/samples/chat-service) | GraphQL subscriptions over WebSocket: a lifecycle hook feeding a custom `onChatEvent` field that only a room's participants may join | `dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api` | 5210 |
| [GraphQL Client](/docs/samples/graphql-client) | Keyed Trax GraphQL clients calling two Trax servers, each request validated against its server's schema before it is sent | `dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient.Gateway` | 5310-5314 |
| [Persisted Operations](/docs/samples/persisted-operations) | An API that runs only stored documents: gated management mutations, a hot-fix by id, the shape-diff guardrail | `dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api` | 5240 |
| [Bookworm](/docs/samples/bookworm) | Cross-schema GraphQL over two domain contexts, owner-scoped rows, and the architecture guards adopted by a consumer | `dotnet run --project samples/Bookworm/Trax.Samples.Bookworm.Api` | 5250 |
| [State Machine](/docs/samples/state-machine) | Two fluent snapshot machines served as typed operations under the `machines` root (the generic `stateMachine` mutations are not served): an exactly-once charge, a forward migration, a server-checked total | `dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api` | 5280 |
| [Energy Hub](/docs/samples/energy-hub) | A hub that schedules and serves GraphQL but runs none of its jobs, and standalone workers that do, with events home over RabbitMQ | hub and worker, see the page | 5202-5203 |
| [Content Shield](/docs/samples/content-shield) | An API that executes nothing: queued and synchronous runs go, signed, to a Lambda-style runner | runner and API, see the page | 5204-5205 |
| [SignalR Broadcaster](/docs/samples/signalr-broadcaster) | Live train events in a browser through the SignalR sink, a hub only signed-in operators may join, a projected failure reason | `dotnet run --project samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster` | 5270 |

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
