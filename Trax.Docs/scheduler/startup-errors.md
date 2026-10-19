---
title: Startup errors
description: "The configuration errors that stop a scheduler host, runner or worker from starting, and the message each one starts with."
type: reference
area: scheduling
nav_order: 20
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder
  - Trax.Scheduler/src/Trax.Scheduler/Extensions/JobRunnerExtensions.cs
---

# Startup errors

Trax checks a host's configuration when the host builds and starts, before any worker takes a job.
Each error below is an `InvalidOperationException` unless the table says otherwise.

## Scheduler

| Cause | Message starts |
|---|---|
| `AddScheduler()` with no data provider | `AddScheduler() requires a data provider (UsePostgres(), UseSqlite(), or UseInMemory()).` |
| A duration or count outside its range, such as `DeleteBatchSize = 20_000` | `The scheduler configuration has values it cannot run with.`, then each value, for example `AddMetadataCleanup: DeleteBatchSize must be between 1 and 10000.` The ranges are on [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler). |
| Manifest groups that depend on each other in a cycle | `Circular dependency detected among manifest groups` |
| Two members of one manifest group stating different values for one group setting | `Manifest group '<group>' is given two different <setting> values` |
| A batch whose prune prefix covers another batch's manifests | `<batch> prunes manifests whose external ID starts with '<prefix>', which includes the manifests of <other batch>` |
| A single `Schedule` whose external id falls inside a batch's prune | `<batch> prunes manifests whose external ID starts with '<prefix>', which includes '<externalId>', scheduled on its own.` |
| `ThenInclude()` with no manifest before it | `ThenInclude() must be called after Schedule(), Include(), or another ThenInclude().` |
| `Include()` or `IncludeMany()` with no `Schedule()` before it | `Include() must be called after Schedule().` (or `IncludeMany()`) |
| A `ThenIncludeMany()` item with no `DependsOn` | `ThenIncludeMany() requires DependsOn to be set on every ManifestItem.` |
| An input whose type is not the one the train takes | `Input type mismatch: <train> expects input of type '<input>'` |
| A train routed to two submitters | `Train '<train>' is routed to multiple submitters` |
| `[TraxRemote]` trains and no `UseRemoteWorkers()`, `UseSqsWorkers()` or `UseLambdaWorkers()` | `'<train>' is marked [TraxRemote], but this scheduler has no remote submitter to send it to` |
| `AddTraxWorker()` on a host whose `AddScheduler()` already runs local workers | `This host registers two local worker pools` |
| A train whose chain asks a decider, with no `AddDecisionRecording()` | Names the trains. See [A host that does not record](/docs/effect/decisions#a-host-that-does-not-record). |

## Runner

| Cause | Message starts |
|---|---|
| `UseTraxJobRunner()` or `UseTraxRunEndpoint()` with no posture | `Mapping the Trax runner endpoint '<route>' requires AddTraxJobRunner(runner => ...) with a SigningKey, an AuthorizationPolicy, or AllowUnsignedRequests().` |
| A runner with a `SigningKey` on a host with no relational data provider and no nonce store | `A Trax runner with a SigningKey keeps the nonces it accepts in the database` |

The postures are described in [Runner authorization posture](/docs/scheduler/runner-authorization).

## Surfaces a scheduler host often carries

| Cause | Message starts |
|---|---|
| `UseTraxApiDashboard()` with no posture | `UseTraxApiDashboard() needs to know who may load the dashboard` |
| `ExposeOperationQueries()` or `ExposeOperationMutations()` with no gate | `ExposeOperationMutations() exposes scheduler-control mutations (...) but the GraphQL endpoint is not gated` |
| `GateOperations()` with no policy or roles | `GateOperations() needs a policy or roles` |
| A `do-not-use-in-production` demo key outside Development | `AddTraxApiKeyAuth() registered a key containing 'do-not-use-in-production'` |

## See also

- [Schedule a train](/docs/scheduler/setup)
- [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
- [Scheduling sample](/docs/samples/scheduling), whose page lists the error each missing piece of
  its `Program.cs` produces
