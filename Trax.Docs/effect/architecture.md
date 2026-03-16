---
layout: default
title: Architecture
parent: Effect
nav_order: 3
---

# Effect Architecture

How the Effect system works internally — the `ServiceTrain` lifecycle, `EffectRunner`, effect providers, and the data layer.

## Trax.Core (Core Engine)

The foundation layer providing Railway Oriented Programming patterns — chaining junctions, propagating errors, and managing Memory.

### Key Classes

```csharp
// Base train class — chains steps and propagates errors
public abstract class Train<TIn, TOut>
{
    public Task<TOut> Run(TIn input);

    public Monad<TIn, TOut> Activate(TIn input, params object[] otherInputs);
}

// Junction interface for individual operations
public interface IJunction<TIn, TOut>
{
    Task<TOut> Run(TIn input);
}

// Chaining is done via methods on Train<TIn, TOut> itself
// e.g. Activate(input).Chain<MyJunction>().Chain<MyOtherJunction>().Resolve()
// See SDK Reference > Train Methods for all overloads
```

This layer handles chaining, error propagation, and the core train lifecycle.

## Trax.Effect (Enhanced Trains)

Extends core trains with dependency injection, metadata tracking, and effect management. `ServiceTrain` wraps every execution with metadata tracking and coordinates effect providers.

### ServiceTrain<TIn, TOut>

```csharp
public abstract class ServiceTrain<TIn, TOut> : Train<TIn, TOut>, IServiceTrain<TIn, TOut>
{
    // Internal framework properties (injected automatically)
    [Inject] public IEffectRunner? EffectRunner { get; set; }
    [Inject] public IJunctionEffectRunner? JunctionEffectRunner { get; set; }
    [Inject] public ILifecycleHookRunner? LifecycleHookRunner { get; set; }
    [Inject] public ILogger<ServiceTrain<TIn, TOut>>? Logger { get; set; }
    [Inject] public IServiceProvider? ServiceProvider { get; set; }

    public Metadata? Metadata { get; internal set; }

    // The canonical (interface) name, set automatically during DI registration
    public string? CanonicalName { get; set; }

    // Prefers CanonicalName (interface name) over the concrete type's FullName
    public string TrainName =>
        CanonicalName ?? GetType().FullName ?? GetType().Name;

    // Parent metadata ID, set automatically for dependent trains
    public long? ParentId { get; internal set; }

    public override async Task<TOut> Run(TIn input, CancellationToken cancellationToken = default)
    {
        // 1. Initialize metadata, save initial state
        this.InitializeServiceTrain();
        await EffectRunner.SaveChanges(CancellationToken);

        // 2. Fire lifecycle hooks
        await LifecycleHookRunner.OnStarted();
        Metadata.SetInputObject(input);

        try
        {
            // 3. Execute the actual train logic
            var result = await RunInternal(input);

            // 4. Handle success or failure from Either result
            result.Match(
                Right: output => Metadata.SetOutputObject(output),
                Left: _ => { }
            );
            await EffectRunner.Update(Metadata);

            // 5. Finalize and save effects
            this.FinishServiceTrain(result);
            await EffectRunner.SaveChanges(CancellationToken);
            await LifecycleHookRunner.OnCompleted();

            return result.Match<TOut>(Right: x => x, Left: ex => throw ex);
        }
        catch (Exception ex)
        {
            // 6. Handle failures and save error state
            this.FinishServiceTrain(Either<Exception, TOut>.Left(ex));
            await EffectRunner.SaveChanges(CancellationToken);
            await LifecycleHookRunner.OnFailed();
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

    public EffectRunner(
        IEnumerable<IEffectProviderFactory> effectProviderFactories,
        IEffectRegistry effectRegistry,
        ILogger<EffectRunner>? logger = null)
    {
        ActiveEffectProviders = [];
        ActiveEffectProviders.AddRange(
            effectProviderFactories
                .Where(factory => effectRegistry.IsEnabled(factory.GetType()))
                .RunAll(factory => factory.Create())
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

This layer adds metadata tracking, effect coordination, and handles the train lifecycle (create metadata -> run junctions -> save effects).

### Canonical Train Naming

When a `ServiceTrain` is resolved through DI (via `AddScopedTraxRoute`, `AddMediator`, etc.), the registration factory sets `CanonicalName` to the **service interface type's `FullName`** (e.g., `MyApp.Trains.IProcessOrderTrain`). This ensures that:

- **Metadata records** always store the interface name, not the concrete class name
- **Work queue entries** reference the interface name, making train resolution stable across refactors of the implementation class
- **Subscriptions and event handlers** match on a single canonical name instead of both service and implementation type names

If a train is instantiated outside of DI (e.g., in tests), `CanonicalName` is `null` and `TrainName` falls back to `GetType().FullName`.

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

Junctions execute inside the "Execute Train Chain" box. Each mutation to the train's `Metadata` is followed by an `Update` call that notifies all registered effect providers — allowing them to react immediately (e.g., `ParameterEffect` re-serializes input/output parameters). The final `SaveChanges` call persists all accumulated side effects. Both success and failure paths call `SaveChanges`, so metadata is always persisted regardless of outcome.

## Data Layer

### DataContext<TDbContext>

```csharp
public class DataContext<TDbContext> : DbContext, IDataContext
    where TDbContext : DbContext
{
    public DbSet<Metadata> Metadatas { get; set; }
    public DbSet<Log> Logs { get; set; }
    public DbSet<Manifest> Manifests { get; set; }
    public DbSet<DeadLetter> DeadLetters { get; set; }
    public DbSet<WorkQueue> WorkQueues { get; set; }
    public DbSet<ManifestGroup> ManifestGroups { get; set; }
    public DbSet<BackgroundJob> BackgroundJobs { get; set; }

    // IEffectProvider implementation
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        await base.SaveChangesAsync(cancellationToken);
    }

    public async Task Track(IModel model)
    {
        Add(model);
    }

    public async Task Update(IModel model)
    {
        base.Update(model);
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
| **JUNCTION_METADATA** | In-memory junction tracking (not persisted to database) |

### Implementation Variants

**PostgreSQL** — ACID transactions, JSON column support, automatic schema migration, PostgreSQL-specific optimizations (enums, JSON queries).

**InMemory** — Fast and lightweight for testing, no external dependencies, API-compatible with production database.
