---
layout: default
title: IJunctionEffectProvider
description: "Reference for IJunctionEffectProvider, code that runs before and after every EffectJunction: its lifecycle, failure behaviour and registration with a factory."
parent: Configuration
grand_parent: SDK Reference
nav_order: 20
---

# IJunctionEffectProvider

A junction effect: code that runs before and after every [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) in a service train. The junction logger and junction progress are junction effects. You write a provider and a factory that creates it, and register the factory with [AddJunctionEffect](/docs/sdk-reference/configuration/add-effect).

## Signature

```csharp
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

| Parameter | Type | Description |
|-----------|------|-------------|
| `effectJunction` | `EffectJunction<TIn, TOut>` | The junction. Its `Metadata` already exists. |
| `serviceTrain` | `ServiceTrain<TTrainIn, TTrainOut>` | The train running it, with its own `Metadata` |
| `cancellationToken` | `CancellationToken` | The train's token |

## Lifecycle

| Call | When |
|------|------|
| `IJunctionEffectProviderFactory.Create()` | When a train's junction effect runner is created, for each factory the effect registry reports enabled at that moment. Toggling an effect affects runners created afterwards. |
| `BeforeJunctionExecution` | Before each `EffectJunction` runs: after its `Metadata` is created, before its start time is set |
| `AfterJunctionExecution` | After each `EffectJunction`, whether it succeeded, failed, or was skipped because an earlier junction failed. `Metadata` then holds the end time, the railway state and `HasRan`. |
| `Dispose()` | When the train is disposed. An exception is logged and the other providers are still disposed. |

Providers run in registration order. A plain `Junction` does not trigger them.

## Failures

An exception from either method is not caught. It stops the providers after it and fails the train. From `AfterJunctionExecution` that is true even when the junction succeeded, and when the junction had already failed, the provider's exception is what the train reports. Catch inside the provider anything that should not fail a run.

## Example

```csharp
public sealed class SlowJunctionWarning(ILogger<SlowJunctionWarning> logger) : IJunctionEffectProvider
{
    public Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> junction, ServiceTrain<TTrainIn, TTrainOut> train, CancellationToken ct) =>
        Task.CompletedTask;

    public Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> junction, ServiceTrain<TTrainIn, TTrainOut> train, CancellationToken ct)
    {
        if (junction.Metadata is { StartTimeUtc: { } start, EndTimeUtc: { } end } && end - start > TimeSpan.FromSeconds(5))
            logger.LogWarning("{Junction} in {Train} took {Elapsed}", junction.Metadata.Name, train.TrainName, end - start);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}

public sealed class SlowJunctionWarningFactory(ILogger<SlowJunctionWarning> logger) : IJunctionEffectProviderFactory
{
    public IJunctionEffectProvider Create() => new SlowJunctionWarning(logger);
}

services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddJunctionEffect<SlowJunctionWarningFactory>()));
```

## Package

```
dotnet add package Trax.Effect
```
