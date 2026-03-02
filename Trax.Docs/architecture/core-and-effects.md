---
layout: default
title: Core & Effects
parent: Architecture
nav_order: 1
---

# Core & Effect System

## Trax.Core (Core Engine)

The foundation layer providing Railway Oriented Programming patterns.

### Key Classes

```csharp
// Base train class
public abstract class Train<TIn, TOut>
{
    public Task<TOut> Run(TIn input);

    protected Train<TIn, TOut> Activate(TIn input);
}

// Step interface for individual operations
public interface IStep<TIn, TOut>
{
    Task<TOut> Run(TIn input);
}

// Chaining is done via methods on Train<TIn, TOut> itself
// e.g. Activate(input).Chain<MyStep>().Chain<MyOtherStep>().Resolve()
// See API Reference > Train Methods for all overloads
```

This layer handles chaining, error propagation, and the core train lifecycle.

## Trax.Effect (Enhanced Trains)

Extends core trains with dependency injection, metadata tracking, and effect management.

### ServiceTrain<TIn, TOut>

```csharp
public abstract class ServiceTrain<TIn, TOut> : Train<TIn, TOut>, IServiceTrain<TIn, TOut>
{
    // Internal framework properties (injected automatically)
    [Inject] public IEffectRunner? EffectRunner { get; set; }
    [Inject] public ILogger<ServiceTrain<TIn, TOut>>? Logger { get; set; }
    [Inject] public IServiceProvider? ServiceProvider { get; set; }

    public Metadata Metadata { get; private set; }
    protected string TrainName => GetType().Name;
    protected int? ParentId { get; set; }

    public sealed override async Task<Either<Exception, TOut>> Run(TIn input)
    {
        // 1. Initialize metadata and start tracking
        Metadata = await InitializeTrain(input);

        try
        {
            // 2. Execute the actual train logic
            var result = await RunInternal(input);

            // 3. Finalize metadata and save effects
            await FinishTrain(result);
            await EffectRunner.SaveChanges(CancellationToken.None);

            return result;
        }
        catch (Exception ex)
        {
            // 4. Handle failures and save error state
            await FinishTrain(Either<Exception, TOut>.Left(ex));
            await EffectRunner.SaveChanges(CancellationToken.None);
            throw;
        }
    }

    protected abstract Task<Either<Exception, TOut>> RunInternal(TIn input);
}
```

### EffectRunner

```csharp
public class EffectRunner : IEffectRunner
{
    private List<IEffectProvider> ActiveEffectProviders { get; init; }

    public EffectRunner(IEnumerable<IEffectProviderFactory> effectProviderFactories)
    {
        ActiveEffectProviders = [];
        ActiveEffectProviders.AddRange(
            effectProviderFactories.RunAll(factory => factory.Create())
        );
    }

    public async Task Track(IModel model)
    {
        ActiveEffectProviders.RunAll(provider => provider.Track(model));
    }

    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        await ActiveEffectProviders.RunAllAsync(
            provider => provider.SaveChanges(cancellationToken)
        );
    }

    public void Dispose() => DeactivateProviders();
}
```

This layer adds metadata tracking, effect coordination, and handles the train lifecycle (create metadata → run steps → save effects).

## Effect Providers Architecture

Effect providers implement the `IEffectProvider` interface to handle different concerns.

### IEffectProvider Interface

```csharp
public interface IEffectProvider : IDisposable
{
    Task SaveChanges(CancellationToken cancellationToken);
    Task Track(IModel model);
    Task Update(IModel model);
}
```

### Available Effect Providers

| Provider | Package | Purpose | Performance Impact |
|----------|---------|---------|-------------------|
| **DataContext** | Trax.Effect.Data | Database persistence | Medium - Database I/O |
| **JsonEffect** | Trax.Effect.Provider.Json | Debug logging | Low - JSON serialization |
| **ParameterEffect** | Trax.Effect.Provider.Parameter | Parameter serialization | Medium - JSON + Storage |
| **Custom Providers** | User-defined | Application-specific | Varies |

## Execution Flow

The full lifecycle of a `ServiceTrain` execution, corresponding to the `Run` method shown above:

```
[Client Request]
       │
       ▼
[TrainBus.RunAsync]
       │
       ▼
[Find Train by Input Type]
       │
       ▼
[Create Train Instance]
       │
       ▼
[Inject Dependencies]
       │
       ▼
[Initialize Metadata] → Track → Update (InProgress)
       │
       ▼
[Set Input] → Update
       │
       ▼
[Execute Train Chain]
       │
       ▼
[Set Output] → Update
       │
       ▼
   Success? ──No──► [FinishTrain: Failed] → Update
       │                      │
      Yes                     │
       │                      │
       ▼                      ▼
[FinishTrain: Completed]   │
  → Update                    │
       │                      │
       └──────────┬───────────┘
                  │
                  ▼
       [SaveChanges - Persist All Effects]
                  │
                  ▼
           [Return Result]
```

Steps execute inside the "Execute Train Chain" box. Each mutation to the train's `Metadata` is followed by an `Update` call that notifies all registered effect providers—allowing them to react immediately (e.g., `ParameterEffect` re-serializes input/output parameters). The final `SaveChanges` call persists all accumulated side effects. Both success and failure paths call `SaveChanges`, so metadata is always persisted regardless of outcome.
