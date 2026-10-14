---
title: Effect architecture
description: "How Trax.Effect works inside: the ServiceTrain lifecycle, EffectRunner, canonical train naming, the effect provider interfaces and the DataContext data layer."
type: explanation
area: effects
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect/Services/ServiceTrain/ServiceTrain.cs
  - Trax.Effect/src/Trax.Effect/Services/EffectRunner/EffectRunner.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/DataContext
---

# Effect architecture

How the Effect system works internally: the `ServiceTrain` lifecycle, `EffectRunner`, effect providers, and the data layer.

## Trax.Core

Chaining, the failure track and Memory belong to `Train<TIn, TOut>` in Trax.Core; see [Core](/docs/core). What follows is what Trax.Effect adds around it.

## Trax.Effect (Enhanced Trains)

Extends core trains with dependency injection, metadata tracking, and effect management. `ServiceTrain` wraps every execution with metadata tracking and coordinates effect providers.

### ServiceTrain<TIn, TOut>

`ServiceTrain` extends `Train` with framework-injected properties and a lifecycle that wraps `Junctions()`:

```csharp fragment
public abstract class ServiceTrain<TIn, TOut> : Train<TIn, TOut>, IServiceTrain<TIn, TOut>
{
    // Injected automatically by the framework
    [Inject] public IEffectRunner? EffectRunner { get; set; }
    [Inject] public IJunctionEffectRunner? JunctionEffectRunner { get; set; }
    [Inject] public ILifecycleHookRunner? LifecycleHookRunner { get; set; }
    [Inject] public ILogger<ServiceTrain<TIn, TOut>>? Logger { get; set; }
    [Inject] public IServiceProvider? ServiceProvider { get; set; }

    public Metadata? Metadata { get; internal set; }
    public string? CanonicalName { get; set; }
    public string TrainName => CanonicalName ?? GetType().FullName ?? GetType().Name;
    public long? ParentId { get; internal set; }

    protected virtual Task<Either<Exception, TOut>> Junctions() => ...;  // Inherited from Train; override to declare the chain
}
```

The `Run` method wraps the train's `Junctions()` declaration with a lifecycle that follows these steps:

1. **Initialize**: Create `Metadata`, set `TrainState.InProgress` and the input, persist via `SaveChanges`. The first write carries the input, so a run that dies before it finishes still has it
2. **Hooks**: Fire `OnStarted` (global lifecycle hooks, then per-train override)
3. **Execute**: Call the train's route definition, producing `Either<Exception, TOut>`
4. **Finalize**: Set output (right track) or exception details (left track), update `TrainState`
5. **Persist**: `SaveChanges` on both tracks. Metadata is always saved regardless of outcome
6. **Post-hooks**: Fire `OnCompleted` or `OnFailed`/`OnCancelled` depending on the result

### EffectRunner

The `EffectRunner` coordinates all registered effect providers. It builds its provider list at construction time by querying `IEffectProviderFactory` instances filtered through the `IEffectRegistry`.

It exposes three operations that fan out to every active provider, awaiting each provider sequentially, and a fourth that claims a pre-created row:
- **`Track(model)`**: Register a new model for tracking (e.g., add `Metadata` to the EF change tracker)
- **`Update(model)`**: Notify providers of an in-memory mutation (e.g., re-serialize parameters after output is set)
- **`SaveChanges(ct)`**: Persist all accumulated changes across all providers
- **`TryClaimPendingRun(metadata, ct)`**: For a run started from a pre-created row, move that row from `Pending` to `InProgress` in the store only if it is still `Pending`, through each provider that implements `IPendingRunClaim` (the data providers do). A refused claim fails the start with `TrainAlreadyStartedException` before the train body runs

A test double for `IEffectRunner` built with NSubstitute or Moq does not inherit the interface's default `TryClaimPendingRun`, which returns `true`: the double implements the member itself and returns `false` until configured, so a run from a pre-created row throws `TrainAlreadyStartedException`. Configure it, for example `runner.TryClaimPendingRun(default!, default).ReturnsForAnyArgs(true)`.

In `Track`, `Update` and `SaveChanges`, a provider that throws does not stop the providers after it. Every provider is called, in registration order, and then the failure is rethrown: one exception as it was, several cancellations as the first of them, anything else as an `AggregateException` carrying each one. So the order you register effects in does not decide whether the run is recorded: with `AddJson()` or a broadcaster registered before `UsePostgres()`, a failure in the first still leaves the data provider to write the terminal state.

The train writes a run's row a second time, with its content replaced by placeholders, only when the store says it refused the row for a value it carries, by throwing `StoreRefusedContentException` (Trax's data contexts do for the Postgres errors that name a value). Any other provider's failure propagates as it is, so a row the store already saved in full is not overwritten. A custom provider that stores the run throws the same exception, around its own error, to take part; a provider that does not store the run should not.

A factory whose `Create()` throws is different: the runner is never built, the train cannot be resolved, and the failure surfaces there rather than as a run with a provider silently missing.

### Canonical Train Naming

When a `ServiceTrain` is resolved through DI (via `AddScopedTraxRoute`, `AddMediator`, etc.), the registration factory sets `CanonicalName` to the **service interface type's `FullName`** (e.g., `MyApp.Trains.IProcessOrderTrain`). This means:

- **Metadata records** always store the interface name, not the concrete class name
- **Work queue entries** reference the interface name, making train resolution stable across refactors of the implementation class
- **Subscriptions and event handlers** match on a single canonical name instead of both service and implementation type names

If a train is instantiated outside of DI (e.g., in tests), `CanonicalName` is `null` and `TrainName` falls back to `GetType().FullName`.

## Effect Providers Architecture

Effect providers implement the `IEffectProvider` interface to handle different concerns.

### IEffectProvider Interface

```csharp fragment
public interface IEffectProvider : IDisposable
{
    Task SaveChanges(CancellationToken cancellationToken);
    Task Track(IModel model);
    Task Update(IModel model);
}
```

The shipped providers are listed on [Choose effect providers](/docs/effect/effect-providers).

## Execution Flow

The full lifecycle of a `ServiceTrain` execution:

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
   Success? --No--> [FinishServiceTrain: Failed or Cancelled] -> Update
       |                      |
      Yes                     |
       |                      |
       v                      v
[FinishServiceTrain: Completed]
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

Junctions execute inside the "Execute Train Chain" box. Each mutation to the train's `Metadata` is followed by an `Update` call that notifies all registered effect providers, allowing them to react immediately (e.g., `ParameterEffect` re-serializes input/output parameters). The final `SaveChanges` call persists all accumulated side effects. Both success and failure paths call `SaveChanges`, so metadata is always persisted regardless of outcome, and one provider failing to save does not keep the others from saving.

## Data Layer

### DataContext

`DataContext<TDbContext>` extends EF Core's `DbContext` and implements both `IDataContext` and `IEffectProvider`. It maps `Track` and `Update` to direct entity state assignment (Added for new entities, Modified for existing), and `SaveChanges` to `SaveChangesAsync`. State is set on the target entity only. Navigation properties are not traversed, which prevents unnecessary UPDATE statements when entities cross DI scope boundaries (e.g., metadata loaded by `LoadMetadataJunction` passed into a child train's context). It also provides transaction support via `BeginTransaction`, `CommitTransaction`, and `RollbackTransaction`. `BeginTransaction(IsolationLevel)` opens the transaction at that level on Postgres and SQLite; InMemory has no transactions of its own and ignores it.

The tables it maps, and how they relate, are on [Trax data model](/docs/effect/data-model).

### Implementation Variants

**PostgreSQL** provides ACID transactions, JSON column support, automatic schema migration, and PostgreSQL-specific optimizations (enums, JSON queries). **SQLite** is file-backed and single-server. **InMemory** is for tests, with no external dependencies and the same `IDataContext` API.
