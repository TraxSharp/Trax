---
layout: default
title: Resolve
parent: Train Methods
grand_parent: SDK Reference
nav_order: 7
---

# Resolve

Extracts the final `TReturn` result from the train. Used in the `RunInternal` path as the **last** method call. Follows a priority chain: exception > short-circuit value > Memory lookup.

> **Note:** When using `Junctions()`, resolution happens automatically via an implicit conversion and you do not call `Resolve` yourself. This method is only needed when overriding `RunInternal`.

## Signatures

### Resolve()

Extracts the result from Memory (or exception/short-circuit).

```csharp
public Either<Exception, TReturn> Resolve()
```

### Resolve(Either\<Exception, TReturn\> returnType)

Returns an explicit result (or the exception if one exists).

```csharp
public Either<Exception, TReturn> Resolve(Either<Exception, TReturn> returnType)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `returnType` | `Either<Exception, TReturn>` | An explicit Either result. Only used if the train has no exception. |

## Returns

`Either<Exception, TReturn>`, the train result. `Left` contains the exception on failure; `Right` contains the `TReturn` value on success.

## Examples

### Standard Usage (RunInternal path)

```csharp
protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input) =>
    Activate(input)
        .Chain<ValidateOrder>()
        .Chain<ProcessPayment>()     // Stores OrderResult in Memory
        .Resolve();                  // Extracts OrderResult from Memory
```

### With Explicit Result

```csharp
protected override async Task<Either<Exception, string>> RunInternal(Unit input)
{
    Either<Exception, string> result = "hello";
    return Activate(input)
        .Resolve(result);    // Returns "hello" (unless an exception occurred)
}
```

## Resolution Priority

`Resolve()` follows this order:

1. **Exception**: If any junction set an exception, return `Left(exception)`.
2. **Short-circuit value**: If [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) captured a value, return `Right(shortCircuitValue)`.
3. **Memory lookup**: Extract `TReturn` from Memory by type.
4. **Fallback**: If `TReturn` is not found in Memory, return `Left(TrainException("Could not find type: ({TReturn})."))`.

## Remarks

- `Resolve()` does not throw. It returns an `Either`. The calling `Run()` method unwraps the Either and throws if needed.
- The parameterized `Resolve(returnType)` is simpler: it returns the exception if one exists, otherwise returns the provided value. It does **not** check short-circuit or Memory.
