---
layout: default
title: Chain
parent: Workflow Methods
grand_parent: API Reference
nav_order: 2
---

# Chain

Executes a step, wiring its input from Memory and storing its output back into Memory. This is the primary method for composing steps into a workflow pipeline. If any step fails (returns `Left`), subsequent steps are short-circuited.

## Chain\<TStep\>()

Creates and executes a step. Input is auto-extracted from Memory. The step's `TIn`/`TOut` types are resolved via reflection from its `IStep<TIn, TOut>` implementation.

```csharp
public Train<TInput, TReturn> Chain<TStep>() where TStep : class
```

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TStep` | `class` | The step type. Must implement `IStep<TIn, TOut>` for some `TIn`/`TOut`. |

This is the overload used in most workflows:

```csharp
return Activate(input)
    .Chain<ValidateOrder>()     // Creates ValidateOrder, extracts its input from Memory
    .Chain<ProcessPayment>()    // Creates ProcessPayment, extracts its input from Memory
    .Resolve();
```

## Chain\<TStep\>(TStep stepInstance)

Executes a pre-created step instance. Input is auto-extracted from Memory.

```csharp
public Train<TInput, TReturn> Chain<TStep>(TStep stepInstance) where TStep : class
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `stepInstance` | `TStep` | A pre-created step instance |

Useful when you need to configure a step before executing it:

```csharp
var step = new ProcessPayment { Gateway = "stripe" };
return Activate(input)
    .Chain<ProcessPayment>(step)
    .Resolve();
```

---

## Behavior

1. If the workflow already has an exception, the step is **skipped** (short-circuited).
2. The step's input is extracted from Memory by type.
3. The step is executed via `RailwayStep`.
4. On **success** (Right): the output is stored in Memory by its type. Tuple outputs are decomposed into individual Memory entries.
5. On **failure** (Left): the exception is captured and all subsequent Chain calls are short-circuited.

## Remarks

- Steps are created and injected with DI services from Memory via `InitializeStep`. Constructor parameters are resolved from Memory by type.
- `TIn`/`TOut` are discovered via reflection from the step's `IStep<,>` interface at runtime.
- See [Memory]({{ site.baseurl }}{% link concepts/memory.md %}) for how type-based lookup works, including tuple handling.
