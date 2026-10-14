---
title: Concurrency limits
description: "Reference for ConcurrentRunLimit, PerPrincipalMaxConcurrentRun, GlobalConcurrentRunLimit and [TraxConcurrencyLimit], which cap concurrent direct train runs."
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 6
package: Trax.Mediator
namespace: Trax.Mediator.Configuration
source:
  - Trax.Mediator/src/Trax.Mediator/Configuration/TraxMediatorBuilder/TraxMediatorBuilder.Concurrency.cs
  - Trax.Mediator/src/Trax.Mediator/Configuration/TraxMediatorBuilder/TraxMediatorBuilder.Settings.cs
  - Trax.Effect/src/Trax.Effect/Attributes/TraxConcurrencyLimitAttribute.cs
---

# Concurrency limits

The methods on `TraxMediatorBuilder` that cap concurrent direct runs per train, per principal and
across every train, and the `[TraxConcurrencyLimit]` attribute that sets a train's own limit. The
limits apply to runs through `ITrainExecutionService.RunAsync` only; queued work is paced by the
scheduler instead.

## Signatures

### Builder methods

```csharp fragment
public TraxMediatorBuilder ConcurrentRunLimit<TTrain>(int maxConcurrent)
    where TTrain : class

public TraxMediatorBuilder PerPrincipalMaxConcurrentRun(int limit)

public TraxMediatorBuilder GlobalConcurrentRunLimit(int maxConcurrent)
```

### Attribute

`TraxConcurrencyLimitAttribute` is in `Trax.Effect.Attributes`, in the Trax.Effect package, and
goes on the concrete train class:
`[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)] public class TraxConcurrencyLimitAttribute(int maxConcurrent) : Attribute`,
with one property, `int MaxConcurrent { get; }`.

## Parameters

| Member | Parameter | Description |
|--------|-----------|-------------|
| `ConcurrentRunLimit<TTrain>` | `TTrain` | The train interface, matched by its `FullName` |
| `ConcurrentRunLimit<TTrain>` | `maxConcurrent` | Concurrent direct runs of that train. Overrides `[TraxConcurrencyLimit]` on it. At least 1 |
| `PerPrincipalMaxConcurrentRun` | `limit` | Concurrent direct runs per principal, across every train. At least 1 |
| `GlobalConcurrentRunLimit` | `maxConcurrent` | Concurrent direct runs across every train. At least 1 |
| `[TraxConcurrencyLimit]` | `maxConcurrent` | Concurrent direct runs of the train it is on, read into `MaxConcurrent`. At least 1 |

## Returns

Each builder method returns the `TraxMediatorBuilder`, for chaining.

## Behavior

- Each method throws `ArgumentOutOfRangeException` for a value below 1, as does the attribute's
  constructor.
- No limit is set by default. A limit that is not set is skipped.
- The three kinds apply independently, and a run takes a permit from every limit that applies to
  it, per train, then per principal, then global, in that order. A train with no per-train limit
  is still held by the global and per-principal ones.
- A run that reaches a limit waits asynchronously for a slot; it is never rejected. Cancelling its
  token while it waits throws `OperationCanceledException` and takes no slot.
- Authorization and input reading happen before a slot is taken.
- Each train has its own per-train budget; a limit on one train does not hold another, except
  through the global and per-principal limits.
- The per-principal limit buckets by the id
  [ICurrentPrincipalProvider](/docs/sdk-reference/mediator-api/i-current-principal-provider)
  returns. The provider `AddMediator` registers returns no id, so the limit holds nothing until
  `AddTraxApi` (called by `AddTraxGraphQL`, after `AddTrax`) replaces it with one that reads the
  `trax:principal-id` claim of the request's authenticated user, or the host registers its own. A
  run with no principal id (anonymous, the scheduler, a remote worker) is not limited per
  principal. A principal's budget exists only while one of its runs holds or waits for a slot.
- The resolved global and per-principal limits are readable on
  [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration); per-train
  limits are not exposed.

## Examples

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(assembly)
        .GlobalConcurrentRunLimit(50)
        .PerPrincipalMaxConcurrentRun(10)
        .ConcurrentRunLimit<IProcessOrderTrain>(15)));
```

```csharp compile
[TraxConcurrencyLimit(15)]
public class ProcessOrderTrain : ServiceTrain<OrderInput, OrderResult>, IProcessOrderTrain
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ChargeOrderJunction>().Resolve();
}
```

## See also

- [Limit concurrent train runs](/docs/mediator/limit-concurrent-train-runs), choosing and setting the limits
- [IConcurrencyLimiter](/docs/sdk-reference/mediator-api/i-concurrency-limiter), the gate that enforces them
- [AddMediator](/docs/sdk-reference/configuration/add-mediator), the rest of the mediator builder
