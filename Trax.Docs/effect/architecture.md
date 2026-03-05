---
layout: default
title: Architecture
parent: Effect
nav_order: 3
---

# Effect Architecture

How the Effect system works internally — the `ServiceTrain` lifecycle, `EffectRunner`, effect providers, and the data layer.

## Trax.Core (Core Engine)

The foundation layer providing Railway Oriented Programming patterns. In the train metaphor, this is the locomotive — the engine that moves data through a chain of steps.

### Key Classes

```csharp
// Base train class — chains steps and propagates errors
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
// See SDK Reference > Train Methods for all overloads
```

This layer handles chaining, error propagation, and the core train lifecycle.

## Trax.Effect (Enhanced Trains)

Extends core trains with dependency injection, metadata tracking, and effect management. Where `Train` is the bare locomotive, `ServiceTrain` is the full commercial service — it logs every journey and coordinates station services (effect providers) along the way.

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

This layer adds metadata tracking, effect coordination, and handles the train lifecycle (create metadata -> run steps -> save effects).

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
       |
       v
[TrainBus.RunAsync]
       |
       v
[Find Train by Input Type]
       |
       v
[Create Train Instance]
       |
       v
[Inject Dependencies]
       |
       v
[Initialize Metadata] -> Track -> Update (InProgress)
       |
       v
[Set Input] -> Update
       |
       v
[Execute Train Chain]
       |
       v
[Set Output] -> Update
       |
       v
   Success? --No--> [FinishTrain: Failed] -> Update
       |                      |
      Yes                     |
       |                      |
       v                      v
[FinishTrain: Completed]      |
  -> Update                   |
       |                      |
       +----------+-----------+
                  |
                  v
       [SaveChanges - Persist All Effects]
                  |
                  v
           [Return Result]
```

Steps execute inside the "Execute Train Chain" box. Each mutation to the train's `Metadata` is followed by an `Update` call that notifies all registered effect providers — allowing them to react immediately (e.g., `ParameterEffect` re-serializes input/output parameters). The final `SaveChanges` call persists all accumulated side effects. Both success and failure paths call `SaveChanges`, so metadata is always persisted regardless of outcome.

## Data Layer

### DataContext<TDbContext>

```csharp
public class DataContext<TDbContext> : DbContext, IDataContext
    where TDbContext : DbContext
{
    public DbSet<Metadata> Metadatas { get; set; }
    public DbSet<ManifestGroup> ManifestGroups { get; set; }
    public DbSet<Log> Logs { get; set; }

    // IEffectProvider implementation
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        await base.SaveChangesAsync(cancellationToken);
    }

    public async Task Track(IModel model)
    {
        Add(model);
    }

    // Transaction support
    public async Task<IDataContextTransaction> BeginTransaction() { }
    public async Task CommitTransaction() { }
    public async Task RollbackTransaction() { }
}
```

### Data Model Structure

```
MANIFEST_GROUP (1:N) -> MANIFEST (1:N) -> METADATA (1:N) -> LOG
                                  |               |
                                  | 1:N           | self-ref (ParentId)
                                  v               |
                            DEAD_LETTER     WORK_QUEUE
                                          BACKGROUND_JOB
```

Key tables:

| Table | Purpose |
|-------|---------|
| **MANIFEST_GROUP** | Per-group dispatch controls (MaxActiveJobs, Priority, IsEnabled) |
| **MANIFEST** | Schedule definition (cron/interval, retry policy, timeout) |
| **METADATA** | Execution record (state, timing, input/output, errors) |
| **LOG** | Application log entries linked to a metadata record |
| **WORK_QUEUE** | Transient queue between scheduling and dispatch |
| **DEAD_LETTER** | Failed jobs that exceeded retry limits |
| **BACKGROUND_JOB** | PostgreSQL job submission queue with visibility timeout |
| **STEP_METADATA** | In-memory step tracking (not persisted to database) |

### Implementation Variants

**PostgreSQL** — ACID transactions, JSON column support, automatic schema migration, PostgreSQL-specific optimizations (enums, JSON queries).

**InMemory** — Fast and lightweight for testing, no external dependencies, API-compatible with production database.
