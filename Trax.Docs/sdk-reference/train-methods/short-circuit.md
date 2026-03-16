---
layout: default
title: ShortCircuit
parent: Train Methods
grand_parent: SDK Reference
nav_order: 4
---

# ShortCircuit

Executes a junction that can **return early** from the train. If the junction succeeds and returns a value of type `TReturn`, that value is captured as the short-circuit result — when [Resolve]({{ site.baseurl }}{% link sdk-reference/train-methods/resolve.md %}) is called, it returns this value instead of looking in Memory.

If the junction **fails** (returns Left), the failure is **ignored** — no exception is set, the train continues normally.

> **Important:** Subsequent `Chain` calls after a successful `ShortCircuit` still execute. The short-circuit value only affects `Resolve()` — it returns the captured value instead of doing a Memory lookup. If you need to skip remaining junctions entirely, combine `ShortCircuit` with a conditional pattern or use the railway error path.

## ShortCircuit\<TJunction\>()

Creates and executes a junction with short-circuit behavior.

```csharp
public Monad<TInput, TReturn> ShortCircuit<TJunction>() where TJunction : class
```

| Type Parameter | Constraint | Description |
|---------------|------------|-------------|
| `TJunction` | `class` | The junction type. Must implement `IJunction<TIn, TOut>` for some `TIn`/`TOut`. |

## ShortCircuit\<TJunction\>(TJunction junctionInstance)

Executes a pre-created junction with short-circuit behavior.

```csharp
public Monad<TInput, TReturn> ShortCircuit<TJunction>(TJunction junctionInstance) where TJunction : class
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `junctionInstance` | `TJunction` | A pre-created junction instance |

## Example

```csharp
protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input)
{
    return Activate(input)
        .ShortCircuit<CheckCache>()       // If cache has result, capture it for Resolve
        .Chain<ValidateOrder>()           // Still executes even on cache hit
        .Chain<ProcessPayment>()          // Still executes even on cache hit
        .Resolve();                       // Returns cached result OR processed result
}
```

## Behavior

1. Creates the junction instance and extracts input from Memory.
2. Executes the junction.
3. If the junction **succeeds** and returns a value of type `TReturn`:
   - The value is stored as `ShortCircuitValue`.
   - `Resolve()` will return this value, bypassing Memory lookup.
4. If the junction **fails** (returns Left): the failure is **ignored** — no exception is set, the train continues normally.

## Remarks

- The key difference from `Chain`: failures do not stop the train. A failing short-circuit junction is silently ignored.
- If the junction output type matches the train's `TReturn`, the value becomes the short-circuit result for `Resolve()`.
- This is useful for cache checks, optional enrichment junctions, and conditional early returns.
