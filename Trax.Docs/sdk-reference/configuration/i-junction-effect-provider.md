---
title: IJunctionEffectProvider
description: "Reference for IJunctionEffectProvider, code that runs before and after every EffectJunction: its lifecycle, failure behaviour and registration with a factory."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 33
package: Trax.Effect
namespace:
  - Trax.Effect.Services.JunctionEffectProvider
  - Trax.Effect.Services.JunctionEffectProviderFactory
source:
  - Trax.Effect/src/Trax.Effect/Services/JunctionEffectProvider/IJunctionEffectProvider.cs
  - Trax.Effect/src/Trax.Effect/Services/JunctionEffectProviderFactory/IJunctionEffectProviderFactory.cs
---

# IJunctionEffectProvider

A junction effect: code that runs before and after every [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) in a service train. The junction logger and junction progress are junction effects. A provider is created by an `IJunctionEffectProviderFactory`, which is registered with [AddJunctionEffect](/docs/sdk-reference/configuration/add-effect).

## Signature

```csharp fragment
namespace Trax.Effect.Services.JunctionEffectProvider;

public interface IJunctionEffectProvider : IDisposable
{
    Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    );

    Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    );
}

namespace Trax.Effect.Services.JunctionEffectProviderFactory;

public interface IJunctionEffectProviderFactory
{
    IJunctionEffectProvider Create();
}
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `effectJunction` | `EffectJunction<TIn, TOut>` | The junction. Its `Metadata` already exists. |
| `serviceTrain` | `ServiceTrain<TTrainIn, TTrainOut>` | The train running it, with its own `Metadata` |
| `cancellationToken` | `CancellationToken` | The train's token |

## Behavior

### Lifecycle

| Call | When |
|------|------|
| `IJunctionEffectProviderFactory.Create()` | When a train's junction effect runner is created, for each factory the effect registry reports enabled at that moment. Toggling an effect affects runners created afterwards. |
| `BeforeJunctionExecution` | Before each `EffectJunction` runs, including one skipped because an earlier junction failed: after its `Metadata` is created, before its start time is set |
| `AfterJunctionExecution` | After each `EffectJunction`, whether it succeeded, failed, or was skipped because an earlier junction failed. `Metadata` then holds the end time, the railway state and `HasRan`. |
| `Dispose()` | When the train is disposed. An exception is logged and the other providers are still disposed. |

Providers run in registration order. A plain `Junction` does not trigger them.

### Failures

An exception from either method stops the providers after it. From `BeforeJunctionExecution` before a junction that will run, and from `AfterJunctionExecution` after a junction that succeeded, it fails the train. From `BeforeJunctionExecution` before a junction that is skipped because an earlier one failed, and from `AfterJunctionExecution` after a junction that failed or was skipped, it is logged as a warning and the failure already on the railway is what the train reports.

## Example

```csharp compile
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.JunctionEffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .AddJunctionEffect<SlowJunctionWarningFactory>()
        .UsePostgres(connectionString))
    .AddMediator(typeof(IShipOrderTrain).Assembly));

public sealed class SlowJunctionWarning(ILogger<SlowJunctionWarning> logger) : IJunctionEffectProvider
{
    public Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> junction, ServiceTrain<TTrainIn, TTrainOut> train, CancellationToken ct) =>
        Task.CompletedTask;

    public Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> junction, ServiceTrain<TTrainIn, TTrainOut> train, CancellationToken ct)
    {
        if (junction.Metadata is { StartTimeUtc: { } start, EndTimeUtc: { } end } metadata
            && end - start > TimeSpan.FromSeconds(5))
            logger.LogWarning("{Junction} in {Train} took {Elapsed}", metadata.Name, train.TrainName, end - start);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}

public sealed class SlowJunctionWarningFactory(ILogger<SlowJunctionWarning> logger) : IJunctionEffectProviderFactory
{
    public IJunctionEffectProvider Create() => new SlowJunctionWarning(logger);
}
```

## See also

- [AddJunctionEffect](/docs/sdk-reference/configuration/add-effect), [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction)
- [Effect Providers](/docs/effect/effect-providers)
