---
title: Limit concurrent train runs
description: "Cap concurrent direct train runs per train, per caller or globally, so a backend with fixed capacity such as Lambda reserved concurrency is not throttled."
type: how-to
area: mediator
nav_order: 20
---

# Limit concurrent train runs

A direct run (`ITrainExecutionService.RunAsync`, which the GraphQL run mutations call) executes as
soon as it is asked for. When a train calls a backend with fixed capacity, such as a Lambda function
with reserved concurrency, a burst of runs past that capacity comes back as throttling (HTTP 429),
retries and slow tails. A concurrency limit holds the extra runs in process until a slot frees up.
Queued work does not need one: the scheduler paces it with `MaxActiveJobs` and
`MaxConcurrentDispatch`.

## 1. Pick the limit

| Situation | Limit |
|-----------|-------|
| One train calls a backend with fixed capacity (a Lambda with 15 reserved concurrency) | A per-train limit on that train, set to the capacity |
| Many trains share one capacity budget | A global limit |
| One authenticated caller must not use up the budget by fanning out requests | A per-principal limit |
| Some trains run locally, some remotely | Per-train limits on the remote trains only; local trains have no outside bottleneck |
| The train is only ever queued | None |

The limits combine: a run waits for every limit that applies to it.

## 2. Set a per-train limit

On the train class:

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

[TraxConcurrencyLimit(15)]
public class ResolveCombatTrain : ServiceTrain<CombatInput, CombatResult>, IResolveCombatTrain
{
    protected override Task<Either<Exception, CombatResult>> Junctions() =>
        Chain<ResolveCombatJunction>().Resolve();
}
```

Or on the mediator builder, which overrides the attribute and keeps the number in configuration:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(IResolveCombatTrain).Assembly)
        .ConcurrentRunLimit<IResolveCombatTrain>(15)));
```

## 3. Set a global or per-principal limit

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(IResolveCombatTrain).Assembly)
        .GlobalConcurrentRunLimit(50)
        .PerPrincipalMaxConcurrentRun(10)));
```

The per-principal limit needs to know who the caller is. On a GraphQL host, call `AddTraxGraphQL`
after `AddTrax` and it does. On any other host, [write a principal provider](/docs/mediator/write-a-principal-provider).

## 4. Keep HTTP retry on for remote trains

A limit stops most oversubscription before it happens; the remote worker's
[retry with backoff](/docs/scheduler/remote-execution) handles the 429, 502 and 503 responses that
still get through, such as during a cold start or a short dip in capacity. Run both.

## SDK Reference

> [Concurrency limits](/docs/sdk-reference/mediator-api/concurrency-limiting) | [IConcurrencyLimiter](/docs/sdk-reference/mediator-api/i-concurrency-limiter) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
