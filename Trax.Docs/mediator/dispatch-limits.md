---
title: Dispatch limits
description: "The mediator's limits on caller-submitted work: runs per principal, input JSON size and OnQueue hook time, with their defaults and refusals."
type: reference
area: mediator
nav_order: 20
source:
  - Trax.Mediator/src/Trax.Mediator/Configuration/MediatorConfiguration.cs
  - Trax.Mediator/src/Trax.Mediator/Configuration/TraxMediatorBuilder/TraxMediatorBuilder.Settings.cs
  - Trax.Mediator/src/Trax.Mediator/Services/ConcurrencyLimiter/ConcurrencyLimiter.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/TrainInputReader.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/TrainExecutionService.cs
  - Trax.Mediator/src/Trax.Mediator/Exceptions/TrainInputValidationException.cs
  - Trax.Mediator/src/Trax.Mediator/Exceptions/QueueHookTimeoutException.cs
---

# Dispatch limits

Three limits on `TraxMediatorBuilder` bound what one caller can make a host do through
`ITrainExecutionService`, the service every caller-built run and enqueue goes through (GraphQL
train fields, the `operations` namespace and the dashboard). Work the scheduler or a remote worker
runs through `ITrainBus` is not subject to them.

| Limit | Builder method | Default | Applies to |
|---|---|---|---|
| Concurrent runs per principal | `PerPrincipalMaxConcurrentRun(int)` | none | `ITrainExecutionService.RunAsync` |
| Input JSON size | `WithMaxInputJsonBytes(int)` | 262,144 bytes (256 KiB) | `RunAsync`, `QueueAsync` and `PrepareAsync` on `ITrainExecutionService`, and so `IOperationsService.QueueTrainAsync` and `RunTrainAsync` |
| `OnQueue` hook duration | `WithMaxQueueHookDuration(TimeSpan)` | 30 seconds | An enqueue's `OnQueue` hook, and the hook a run started through `IOperationsService.RunTrainAsync` runs |

The values are read back from [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration).

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .PerPrincipalMaxConcurrentRun(10)
        .WithMaxInputJsonBytes(64 * 1024)
        .WithMaxQueueHookDuration(TimeSpan.FromSeconds(10))));
```

## Concurrent runs per principal

| | |
|---|---|
| Argument | A positive number of runs. Zero or less throws `ArgumentOutOfRangeException` ("PerPrincipalMaxConcurrentRun must be positive.") at the builder call. |
| Counted | Runs through `ITrainExecutionService.RunAsync` that are executing or waiting. Queued work is not counted. |
| Principal | The id `ICurrentPrincipalProvider.GetCurrentPrincipalId()` returns, compared ordinally. `AddMediator` registers a provider that returns `null`, so the limit has no effect until another is registered. `AddTraxApi` (which `AddTraxGraphQL` calls) registers one that returns the authenticated request user's `trax:principal-id` claim. |
| No principal | A run with a `null` or empty id (an anonymous request, a run with no request) is not limited per principal. |
| Over the limit | The run waits on its principal's semaphore until one of that principal's runs finishes. It is not refused. |
| Order | A run takes its per-train slot, then its per-principal slot, then the global slot, and releases them in reverse. |
| Other limits | The per-train and global limits apply as well; see [Concurrency limiting](/docs/sdk-reference/mediator-api/concurrency-limiting). |

The principal id is scheme-qualified (`TraxApiKey:alice`, `TraxJwt:alice`), so one person signed in
through two schemes has two budgets. A custom provider must return an id the caller cannot choose:
see [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider).

## Input JSON size

| | |
|---|---|
| Argument | A positive number of bytes. Zero or less throws `ArgumentOutOfRangeException` ("MaxInputJsonBytes must be positive."). |
| Measured | The UTF-8 byte length of the caller's input JSON, before it is parsed. |
| When | After authorization and before deserialization: a caller who may not run the train is refused for that first, and an oversized input never reaches the deserializer. |
| Stored form | An enqueue writes the parsed input back out (indented, every member present) onto its work queue entry. That form is capped at 4 times the limit (`TrainInputReader.StoredInputGrowthFactor`), checked before the entry is written; writing it stops as soon as it crosses the cap. |
| Refusal | `TrainInputValidationException`, message "The train input failed validation.". `TrainName`, `ObservedBytes` and `MaxBytes` carry the detail for logs. |
| Over GraphQL | Code `TRAX_INVALID_INPUT`, with the same message. Through `IOperationsService` (the `operations` namespace, the dashboard), a failed result with a message. |

Input is read without JSON reference handling: `$id`, `$ref` and `$values` are not honoured, so the
parsed input is the tree the caller sent. A property given twice, in any casing, is refused. The
reading rules are listed under [TrainInputReader](/docs/sdk-reference/mediator-api/i-train-execution-service).

A remote runner's HTTP entry points read at most `TraxJobRunnerOptions.MaxRequestBodyBytes`
(8 MiB by default), which holds an input at the default stored-form cap. Raise both together.

## OnQueue hook duration

| | |
|---|---|
| Argument | A positive duration of at most `int.MaxValue` milliseconds, or `Timeout.InfiniteTimeSpan` for no limit. Anything else throws `ArgumentOutOfRangeException`. |
| Measured | How long the hook runs while its enqueue holds a pooled connection and an open transaction. |
| At the limit | The hook's cancellation token is cancelled and the enqueue stops waiting for it. The enqueue rolls back everything the hook wrote on the enqueue's context and releases the connection. |
| Refusal | `QueueHookTimeoutException`, message `<train>.OnQueue ran longer than <n>s, the limit set by MaxQueueHookDuration, so the enqueue was rolled back. ...`. `TrainName` and `Limit` carry the detail. A warning is logged. |
| Through `IOperationsService` | A failed result whose message includes the exception's. |
| A hook that keeps running | It is not stopped. It runs on without the enqueue's context or transaction, a side effect it writes elsewhere may still land, and an enqueue it starts from then on is refused. A hook that blocks its thread rather than awaiting is not interrupted until it yields. |
| Deferring trains | A train that defers queue promotion (`DeferQueuePromotion`) runs its hook holding no connection, and this limit does not apply to it. |

The hook itself is described under
[OnQueue](/docs/mediator/enqueue-hooks).

## See also

- [Secure a GraphQL host](/docs/security/secure-a-graphql-host): where these limits sit among a host's other guards.
- [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration): the properties.
- [Concurrency limiting](/docs/sdk-reference/mediator-api/concurrency-limiting): per-train and
  global limits.

## SDK Reference

> [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration) | [Concurrency limiting](/docs/sdk-reference/mediator-api/concurrency-limiting) | [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)
