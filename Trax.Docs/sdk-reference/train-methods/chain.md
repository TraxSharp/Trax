---
layout: default
title: Chain
parent: Train Methods
grand_parent: SDK Reference
nav_order: 2
---

# Chain

Executes a junction, wiring its input from Memory and storing its output back into Memory. This is the primary method for composing junctions into a train pipeline. If any junction fails (returns `Left`), subsequent junctions are short-circuited.

Every `Chain` overload is a protected method on `Train<TInput, TReturn>` and returns a `MonadTask<TInput, TReturn>`, an awaitable wrapper around the chain that carries the same `Chain`, `IChain`, `ShortCircuit`, `Extract`, `AddServices` and `Resolve` methods, so calls continue fluently across async junctions. A chain always ends in `.Resolve()`.

## Chain\<TJunction\>()

Creates and executes a junction. Input is auto-extracted from Memory. The junction's `TIn`/`TOut` types are resolved via reflection from its `IJunction<TIn, TOut>` implementation.

```csharp
protected MonadTask<TInput, TReturn> Chain<TJunction>() where TJunction : class
```

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TJunction` | `class` | The junction type. Must implement `IJunction<TIn, TOut>` for some `TIn`/`TOut`. |

This is the overload used in most trains:

```csharp
protected override Task<Either<Exception, OrderResult>> Junctions() =>
    Chain<ValidateOrder>()          // Creates ValidateOrder, extracts its input from Memory
        .Chain<ProcessPayment>()    // Creates ProcessPayment, extracts its input from Memory
        .Resolve();
```

## Chain\<TJunction\>(TJunction junctionInstance)

Executes a pre-created junction instance. Input is auto-extracted from Memory.

```csharp
protected MonadTask<TInput, TReturn> Chain<TJunction>(TJunction junctionInstance) where TJunction : class
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `junctionInstance` | `TJunction` | A pre-created junction instance |

Useful when you need to configure a junction before executing it:

```csharp
protected override Task<Either<Exception, OrderResult>> Junctions() =>
    Chain(new ProcessPayment { Gateway = "stripe" }).Resolve();
```

## Chain\<TJunction, TIn, TOut\>() and Chain\<TJunction, TIn\>()

Explicit-typed overloads for a junction whose input and output types cannot be inferred from its interface, for example one that implements `IJunction<,>` more than once. The types are stated rather than discovered by reflection.

```csharp
protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
    where TJunction : IJunction<TIn, TOut>

protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
    where TJunction : IJunction<TIn, TOut>, new()

// Unit output
protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
    where TJunction : IJunction<TIn, Unit>

protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
    where TJunction : IJunction<TIn, Unit>, new()
```

| Type Parameter | Description |
|---------------|-------------|
| `TJunction` | The junction type |
| `TIn` | The input type taken from Memory |
| `TOut` | The output type stored in Memory (`Unit` for the two-parameter form) |

The parameterless forms construct the junction with `new()`, so they take no constructor dependencies. Pass an instance when the junction needs them.

---

## Behavior

1. If the train already has an exception, the junction is **skipped** (short-circuited).
2. The junction's input is extracted from Memory by type.
3. The junction is executed via `RailwayJunction`.
4. On **success** (Right): the output is stored in Memory by its type. Tuple outputs are decomposed into individual Memory entries.
5. On **failure** (Left): the exception is captured and all subsequent Chain calls are short-circuited.

## Remarks

- Junctions are created and injected with DI services from Memory via `InitializeJunction`. Constructor parameters are resolved from Memory by type.
- `TIn`/`TOut` are discovered via reflection from the junction's `IJunction<,>` interface at runtime.
- See [Memory](/docs/core/memory) for how type-based lookup works, including tuple handling.

## SynchronizationContext Safety

The chain is asynchronous end to end: each junction is awaited with `ConfigureAwait(false)`, and nothing blocks on a junction's task. That keeps chains safe in environments with a single-threaded `SynchronizationContext` such as Blazor Server, WPF, WinForms, and legacy ASP.NET.
