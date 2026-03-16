---
layout: default
title: Chain
parent: Train Methods
grand_parent: SDK Reference
nav_order: 2
---

# Chain

Executes a junction, wiring its input from Memory and storing its output back into Memory. This is the primary method for composing junctions into a train pipeline. If any junction fails (returns `Left`), subsequent junctions are short-circuited.

## Chain\<TJunction\>()

Creates and executes a junction. Input is auto-extracted from Memory. The junction's `TIn`/`TOut` types are resolved via reflection from its `IJunction<TIn, TOut>` implementation.

```csharp
public Monad<TInput, TReturn> Chain<TJunction>() where TJunction : class
```

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TJunction` | `class` | The junction type. Must implement `IJunction<TIn, TOut>` for some `TIn`/`TOut`. |

This is the overload used in most trains:

```csharp
return Activate(input)
    .Chain<ValidateOrder>()     // Creates ValidateOrder, extracts its input from Memory
    .Chain<ProcessPayment>()    // Creates ProcessPayment, extracts its input from Memory
    .Resolve();
```

## Chain\<TJunction\>(TJunction junctionInstance)

Executes a pre-created junction instance. Input is auto-extracted from Memory.

```csharp
public Monad<TInput, TReturn> Chain<TJunction>(TJunction junctionInstance) where TJunction : class
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `junctionInstance` | `TJunction` | A pre-created junction instance |

Useful when you need to configure a junction before executing it:

```csharp
var junction = new ProcessPayment { Gateway = "stripe" };
return Activate(input)
    .Chain<ProcessPayment>(junction)
    .Resolve();
```

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
- See [Memory]({{ site.baseurl }}{% link core/memory.md %}) for how type-based lookup works, including tuple handling.

## SynchronizationContext Safety

Chain suppresses the current `SynchronizationContext` when awaiting an incomplete junction task via `.GetAwaiter().GetResult()`. This prevents deadlocks in environments with a single-threaded `SynchronizationContext` such as Blazor Server, WPF, WinForms, and legacy ASP.NET. The original context is restored after the junction completes (or throws).
