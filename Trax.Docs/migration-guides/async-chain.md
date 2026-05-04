# Async Chain Migration

Trax 1.x ships `Junctions()`, `Chain`, `Resolve`, `Extract`, `ShortCircuit`, and `AddServices` as async-by-default. The same names you used before are still there, the fluent shape is unchanged, but the return types now wrap in `Task` so the framework no longer blocks on async work mid-chain.

## Why

The old chain blocked synchronously on each junction's `Task` (sync-over-async) so it could keep returning `Monad<,>` and let the implicit `operator TReturn(Monad<,>)` produce the final value. That deadlocked under Blazor Server, WPF, and any other single-threaded `SynchronizationContext`. Patching the suppression hack only ever moved the deadlock around. The fix is to make the chain async all the way down.

## What changed

| Member | Before | After |
|---|---|---|
| `Train.Junctions()` | returns `TReturn` | returns `Task<Either<Exception, TReturn>>` |
| `Monad.Chain<T>()` | returns `Monad<,>` | returns `MonadTask<,>` (awaitable wrapper, see below) |
| `Monad.ShortCircuit<T>()` | returns `Monad<,>` | returns `MonadTask<,>` |
| `Monad.Extract<TIn, TOut>()` | returns `Monad<,>` | unchanged on `Monad<,>`, also available on `MonadTask<,>` |
| `Monad.AddServices<...>(...)` | returns `Monad<,>` | unchanged on `Monad<,>`, also available on `MonadTask<,>` |
| `Monad.Resolve()` | returns `Either<Exception, TReturn>` | unchanged on `Monad<,>`, returns `Task<Either<Exception, TReturn>>` on `MonadTask<,>` |
| `Train.RunInternal` default | sync wrap of `Junctions()` | `=> Junctions()` (the symmetric case) |
| `implicit operator TReturn(Monad<,>)` | existed | **deleted** (C# forbids `implicit operator Task<T>`) |
| Internal `Chain<TJunction, TIn, TOut>(... out Either<,>)` overloads | existed | **deleted** (`out` is illegal in `async`) |
| `SynchronizationContext` suppression in `Monad.Chain` | existed (band-aid) | **deleted** (deadlock class is structurally impossible now) |

`MonadTask<,>` is an awaitable struct that wraps `Task<Monad<,>>` and exposes the same Chain/ShortCircuit/Extract/AddServices/Resolve surface so `.Chain<A>().Chain<B>().Resolve()` keeps composing across async boundaries. The wrapper exists because C# does not perform partial generic inference between explicit type arguments and the receiver type, so a plain extension method on `Task<Monad<,>>` would force callers to specify all three generic parameters on every link.

## Migration

Both override styles map mechanically.

### Pattern A: overriding `Junctions()`

Before:

```csharp
public class LookupTrain : ServiceTrain<LookupInput, LookupOutput>, ILookupTrain
{
    protected override LookupOutput Junctions() =>
        Chain<LoadInputJunction>().Chain<DispatchJunction>();
}
```

After:

```csharp
public class LookupTrain : ServiceTrain<LookupInput, LookupOutput>, ILookupTrain
{
    protected override Task<Either<Exception, LookupOutput>> Junctions() =>
        Chain<LoadInputJunction>().Chain<DispatchJunction>().Resolve();
}
```

Three changes per train: wrap the return type in `Task<Either<Exception, ...>>`, append a `.Resolve()` to the chain, and add `using LanguageExt;` if the file did not already import it.

### Pattern B: overriding `RunInternal`

Before:

```csharp
public class LookupTrain : ServiceTrain<LookupInput, LookupOutput>, ILookupTrain
{
    protected override async Task<Either<Exception, LookupOutput>> RunInternal(LookupInput input) =>
        Activate(input).Chain<LoadInputJunction>().Chain<DispatchJunction>().Resolve();
}
```

After:

```csharp
public class LookupTrain : ServiceTrain<LookupInput, LookupOutput>, ILookupTrain
{
    protected override Task<Either<Exception, LookupOutput>> RunInternal(LookupInput input) =>
        Activate(input).Chain<LoadInputJunction>().Chain<DispatchJunction>().Resolve();
}
```

Drop the `async` keyword, since the chain itself already returns `Task<Either<,>>`.

### Patterns that need a real edit

If the body wrapped a sync chain in `Task.FromResult`:

```csharp
protected override Task<Either<Exception, T>> RunInternal(T input) =>
    Task.FromResult(Activate(input).Chain<X>().Resolve());
```

The inner `.Resolve()` now already returns `Task<Either<,>>`, so the wrap is redundant and needs removing:

```csharp
protected override Task<Either<Exception, T>> RunInternal(T input) =>
    Activate(input).Chain<X>().Resolve();
```

If the chain ends without any `Chain` call (just `Activate` + `Extract` or just `Activate`), `Resolve()` is still synchronous on `Monad<,>`, so wrap with `Task.FromResult`:

```csharp
protected override Task<Either<Exception, T>> RunInternal(T input) =>
    Task.FromResult(Activate(input).Extract<TWrapper, T>().Resolve());
```

If you used the implicit conversion `T result = monad;`, replace it with an explicit `await monad.Chain<X>().Resolve();` and inspect the resulting `Either<,>`.

## Junction implementations

`Junction<TIn, TOut>.Run` was already `async Task<TOut>`. **Nothing changes** in junction code.

## What you can delete from your codebase

- Any `[CancelAfter]` or sync-context workarounds you added specifically to dodge the chain deadlock under Blazor Server.
- Tests that reproduce the deadlock symptom: with the new architecture the deadlock cannot happen by construction.

## Versioning

This is a Trax minor bump. Consumers pinned via `Version="1.*"` will pull the new minor and need the mechanical edits above before their projects compile. Plan the upgrade in a single PR per consumer.
