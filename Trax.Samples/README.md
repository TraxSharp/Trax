# Trax.Samples

[![Build](https://github.com/TraxSharp/Trax/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax/actions/workflows/release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Samples.Templates)](https://www.nuget.org/packages/Trax.Samples.Templates)
[![codecov](https://codecov.io/gh/TraxSharp/Trax/branch/main/graph/badge.svg?flag=Trax.Samples)](https://app.codecov.io/gh/TraxSharp/Trax?flags%5B0%5D=Trax.Samples)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/samples)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

`Trax.Samples` holds complete Trax sample apps and the `dotnet new` templates for an API, a scheduler or a hub. It sits
last in the stack and uses every other layer; the templates ship as
[Trax.Samples.Templates](https://www.nuget.org/packages/Trax.Samples.Templates), and `trax generate` in
[Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli) builds on the hub template.

## Example

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-hub -n MyApp      # or trax-api, trax-scheduler
cd MyApp && dotnet run            # http://localhost:5400/trax/graphql and /trax
dotnet test tests/MyApp.Tests
```

Each template stores runs in memory, so it needs no database, and ships a README and a test project; swap
`UseInMemory()` for `UsePostgres(...)` to keep runs.

## Samples

Each sample is a complete app under `samples/` that proves one major Trax feature, with an end-to-end suite under
`tests/` that runs it against a real host. Each has a page under [traxsharp.net/docs/samples](https://traxsharp.net/docs/samples).

| Sample | What it shows | Storage |
|---|---|---|
| [ChatService](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/ChatService/README.md) | A chat app: mutation trains, and a lifecycle hook that publishes each result to room-scoped GraphQL subscriptions over WebSockets, with a React client | SQLite |
| [Bookworm](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Bookworm) | Two domains, each with its own schema and DbContext, joined by a cross-schema GraphQL edge; also adopts the architecture-guard fixtures | Postgres |
| [GraphQLClient](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/GraphQLClient/README.md) | Two Trax servers called through keyed Trax GraphQL clients, each request validated against its server's schema before it is sent; plus the raw, resource and typed query modes | In memory |
| [EnergyHub](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/DistributedWorkers/README.md) | A hub (GraphQL, scheduler, dashboard) that runs none of the jobs it queues, standalone workers that do, and their events reaching the hub's subscriptions over RabbitMQ | Postgres, RabbitMQ |
| [ContentShield](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/EphemeralWorkers/README.md) | An API that executes nothing: queued and synchronous work goes, signed, to a runner that is a Lambda function in production and a local server in development | Postgres, RabbitMQ |
| [PersistedOperations](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/PersistedOperations) | An API that accepts only persisted operations, and a client that uploads, queries and hot-fixes them | Postgres |
| [StateMachine](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/StateMachine/README.md) | Two snapshot state machines served over GraphQL: an exactly-once charge, a forward migration and a server-checked total, with a React client | Postgres |
| [SignalRBroadcaster](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/SignalRBroadcaster/README.md) | Live train events in a browser through the SignalR sink, a hub only signed-in operators may join, and a failure reason shown only when the train wrote it for clients | In memory |
| [Auth](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/Auth/Trax.Samples.Auth/README.md) | Securing a Trax server end to end: API keys and JWT side by side, `[TraxAuthorize]` roles and policies, `GateOperations`, scheme-qualified principal ids and an audit trail | Postgres |
| [Recovery](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/Recovery/README.md) | A train asks a model, a later step crashes, and the manifest's retry replays the recorded decisions instead of asking again, shown live on a React page from junction events; a topic map runs three signals in parallel branches and crashes one; two state machines run trains, one system-owned instance per index partition and a user's topic map wizard | Postgres |
| [Scheduling](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/Scheduling/README.md) | Interval, cron, one-off, dependent and dormant manifests; a train that retries with backoff, dead-letters, and runs again when its dead letter is requeued over GraphQL | Postgres |

Every Postgres sample has a database of its own (`trax_auth`, `trax_bookworm`, `trax_contentshield`, `trax_energyhub`,
`trax_persisted_operations`, `trax_recovery`, `trax_scheduling`, `trax_statemachine`), because a scheduler polls every
manifest and job in its database and would fail another sample's. `docker compose up -d` creates them on a fresh
volume. Its init script runs only once, so on a volume from before a database was added, create it yourself:
`docker exec trax_database createdb -U trax trax_scheduling`.

## Feature coverage

Each major Trax feature, and the test that proves it end to end. A feature with no sample here is proven by a test in
its own repo. There is no sample for Trax.Core: every sample uses it. `FeatureCoverageTableTests` checks that each class
named for this repo exists; why the samples are shaped this way is
[ADR 0006](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/docs/adr/0006-one-sample-per-major-feature-proven-end-to-end.md).

| Area | Feature | Proved by |
|---|---|---|
| Scheduling | Interval and cron manifests | [`IntervalScheduleTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/IntervalScheduleTests.cs), [`CronScheduleTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/CronScheduleTests.cs) |
|  | One-off, dependent and dormant manifests | [`ScheduleOnceTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/ScheduleOnceTests.cs), [`DependentManifestTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/DependentManifestTests.cs), [`DormantDependentTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/DormantDependentTests.cs) |
|  | Retries with backoff, dead letters | [`RetryAndDeadLetterTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/RetryAndDeadLetterTests.cs) |
|  | Dead-letter requeue and acknowledge over GraphQL | [`DeadLetterOperationsTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/OperationsTests/DeadLetterOperationsTests.cs) |
|  | Per-train metadata retention | [`MetadataCleanupTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/MetadataCleanupTests.cs) |
|  | Queue-hook time limit (`MaxQueueHookDuration`) | Trax.Mediator [`QueueHookTimeLimitTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Mediator/tests/Trax.Mediator.Tests.Postgres.Integration/IntegrationTests/QueueHookTimeLimitTests.cs) |
| Operations | Manifests, executions, trigger, disable and enable | [`ManifestOperationsTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Scheduling.E2E/OperationsTests/ManifestOperationsTests.cs) |
|  | Run-train operation (`operations.workQueue.runTrain`) | Trax.Api [`RunTrainMutationTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/tests/Trax.Api.Tests/RunTrainMutationTests.cs) |
|  | Batch operations (cancel, enable, disable a selection) | Trax.Api [`OperationsBatchMutationsTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/tests/Trax.Api.Tests/OperationsBatchMutationsTests.cs) |
|  | Preparing a train's input without running it (`ITrainExecutionService.PrepareAsync`) | Trax.Mediator [`PrepareTrainTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Mediator/tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/PrepareTrainTests.cs) |
| API and auth | API keys and JWT side by side, scheme-qualified principal ids | [`AuthenticationTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/AuthenticationTests.cs) |
|  | `[TraxAuthorize]` on trains and query models | [`TrainAuthorizationTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/TrainAuthorizationTests.cs), [`QueryModelAuthorizationTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/QueryModelAuthorizationTests.cs) |
|  | `GateOperations` | [`OperationsGateTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/OperationsGateTests.cs) |
|  | Audit trail | [`AuditTrailTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/AuditTrailTests.cs) |
|  | Demo credentials exist only in Development | [`DemoCredentialsEnvironmentTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/DemoCredentialsEnvironmentTests.cs) |
|  | Dashboard posture options (`RequirePolicy`, `RequireRoles`, `AllowAnonymousDashboard`) | Trax.Dashboard [`DashboardAuthorizationTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Dashboard/tests/Trax.Dashboard.Tests.Integration/UnitTests/DashboardAuthorizationTests.cs), [`TemplateEnvironmentTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Templates.Tests/IntegrationTests/TemplateEnvironmentTests.cs) |
| GraphQL | Keyed, schema-validated GraphQL clients | [`KeyedClientE2ETests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.GraphQLClient.E2E/KeyedClientE2ETests.cs) |
|  | Cross-schema edges between two domains | [`CrossSchemaEdgeTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Bookworm.E2E/ApiTests/CrossSchemaEdgeTests.cs) |
|  | Persisted operations: enforcement and hot fixes | [`EnforcementTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.PersistedOperations.E2E/ApiTests/EnforcementTests.cs), [`HotFixFlowTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.PersistedOperations.E2E/ApiTests/HotFixFlowTests.cs) |
|  | Persisted operations by tenant | [`AdditionalCoverageTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.PersistedOperations.E2E/ApiTests/AdditionalCoverageTests.cs), Trax.Dashboard [`PersistedOperationsTenantTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Dashboard/tests/Trax.Dashboard.Tests.Integration/UnitTests/Components/PersistedOperationsTenantTests.cs) |
|  | GraphQL over GET, off unless the host opts in | Trax.Api [`GraphQLGetRequestsTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/tests/Trax.Api.Tests/GraphQLGetRequestsTests.cs) |
| Real time | GraphQL subscriptions over WebSockets | [`ChatEventSubscriptionTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.ChatService.E2E/ChatApiTests/ChatEventSubscriptionTests.cs) |
|  | Lifecycle subscriptions (`[TraxBroadcast]`), and their refusal when a caller may see no train | [`SubscriptionAuthTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/SubscriptionAuthTests.cs), [`LifecycleSubscriptionTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.ChatService.E2E/ChatApiTests/LifecycleSubscriptionTests.cs) |
|  | Credentials on a WebSocket (`connection_init`) | [`SubscriptionAuthTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Auth.E2E/Tests/SubscriptionAuthTests.cs) |
|  | Allowed WebSocket origins (`AllowSocketOrigins`) | Trax.Api [`SocketUpgradeOriginTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/tests/Trax.Api.Tests/SocketUpgradeOriginTests.cs) |
|  | SignalR broadcaster | [`LiveEventTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.SignalRBroadcaster.E2E/HubTests/LiveEventTests.cs), [`HubPostureTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.SignalRBroadcaster.E2E/HubTests/HubPostureTests.cs) |
|  | Junction events (`onJunctionEvent`, `operations.junctionRuns`) | [`BroadcastViewTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/BroadcastViewTests.cs), [`RetryReplayTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/RetryReplayTests.cs) |
| Data | Owner scopes on per-user data | [`LendingOwnershipTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Bookworm.E2E/ApiTests/LendingOwnershipTests.cs) |
|  | `[TraxSensitive]` members masked in every copy of an input | Trax.Api [`SensitiveInputCopiesTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/tests/Trax.Api.Tests/SensitiveInputCopiesTests.cs) |
| Execution topologies | A hub that queues, workers that run, events over RabbitMQ | [`HubExecutesNoTrainsTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.EnergyHub.E2E/HubTests/HubExecutesNoTrainsTests.cs), [`CrossProcessEventTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.EnergyHub.E2E/HubTests/CrossProcessEventTests.cs) |
|  | Ephemeral runners (Lambda in production, a local server in development) | [`RemoteExecutionTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.ContentShield.E2E/ApiTests/RemoteExecutionTests.cs) |
| State machines | Fluent machines driven over the `stateMachine` mutations; illegal transitions refused | [`TurnstileTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.StateMachine.E2E/Tests/TurnstileTests.cs), [`CheckoutTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.StateMachine.E2E/Tests/CheckoutTests.cs) |
|  | Exactly-once effect (`RunsOnce`), however often the send is repeated | [`CheckoutTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.StateMachine.E2E/Tests/CheckoutTests.cs) |
|  | Forward migration of stored drafts (`MigrateFrom`) | [`ForwardMigrationTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.StateMachine.E2E/Tests/ForwardMigrationTests.cs) |
|  | Server-owned values: the client cannot set what the effect charges | [`ServerOwnedTotalTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.StateMachine.E2E/Tests/ServerOwnedTotalTests.cs) |
|  | Drafts per caller; anonymous refused; demo keys only in Development | [`AuthenticationTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.StateMachine.E2E/Tests/AuthenticationTests.cs), [`TurnstileTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.StateMachine.E2E/Tests/TurnstileTests.cs) |
|  | A state that invokes a train: system-owned instances started from a train, an unsure output routed by a guarded `OnDone`, a failed run retried by entering the state again | [`SourcePartitionMachineTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/SourcePartitionMachineTests.cs) |
|  | A user's wizard whose state runs a train: only the run's outcome moves it on, a hand-fired outcome and an autosave into a result are refused, and the draft is rebuilt after its host is killed mid-run | [`TopicMapMachineTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/TopicMapMachineTests.cs), [`RestartTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/RestartTests.cs) |
| Decisions and recovery | Train decisions replayed by a manifest's retry | [`RetryReplayTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/RetryReplayTests.cs), Trax.Scheduler [`ManifestRetryReplaysDecisionsTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Scheduler/tests/Trax.Scheduler.Tests.Integration/IntegrationTests/ManifestRetryReplaysDecisionsTests.cs) |
|  | Asking afresh, and requeue replaying by default | [`AskAfreshTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/AskAfreshTests.cs) |
| Parallel branches | Named branches side by side in one run, a failed branch failing the run by name, and the join as the one step that writes | [`TopicMapTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Recovery.E2E/RecoveryTests/TopicMapTests.cs) |
| Templates | `trax-api`, `trax-scheduler`, `trax-hub`: scaffold, build, test, start | [`ScaffoldedTemplateTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Templates.Tests/IntegrationTests/ScaffoldedTemplateTests.cs), [`ScaffoldedTemplateRestoreTests`](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/tests/Trax.Samples.Templates.Tests/IntegrationTests/ScaffoldedTemplateRestoreTests.cs) |

## Where this fits

Trax is split into layers, one folder of this repository each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Samples.**

| Folder | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax/tree/main/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax/tree/main/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax/tree/main/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| **[Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples)** | **Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates** |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
