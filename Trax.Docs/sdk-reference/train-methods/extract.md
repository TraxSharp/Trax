---
layout: default
title: Extract
parent: Train Methods
grand_parent: SDK Reference
nav_order: 5
---

# Extract

Pulls a nested property or field out of a Memory object into its own Memory slot. Uses reflection to find the first public instance property or field of type `TOut` on the `TIn` object.

## Signatures

### Extract\<TIn, TOut\>()

Retrieves `TIn` from Memory, then extracts a `TOut` property/field from it.

```csharp
public Monad<TInput, TReturn> Extract<TIn, TOut>()
```

### Extract\<TIn, TOut\>(TIn input)

Extracts a `TOut` property/field from the provided `TIn` object.

```csharp
public Monad<TInput, TReturn> Extract<TIn, TOut>(TIn input)
```

## Type Parameters

| Type Parameter | Description |
|---------------|-------------|
| `TIn` | The source type to extract from. Must have a public instance property or field of type `TOut`. |
| `TOut` | The target type to extract and store in Memory. |

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `TIn` | No | The source object (second overload only). If omitted, `TIn` is retrieved from Memory. |

## Returns

`Monad<TInput, TReturn>`, the train instance, for fluent chaining.

## Example

```csharp
public record OrderInput(string CustomerId, OrderDetails Details);
public record OrderDetails(string ItemId, int Quantity);

protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Extract<OrderInput, OrderDetails>()       // Pulls OrderDetails out of OrderInput
        .Chain<ProcessOrder>().Resolve();               // ProcessOrder receives OrderDetails from Memory
```

## Behavior

1. Looks for the first public instance **property** of type `TOut` on `TIn`. Static properties and indexers are skipped: a static value is not this object's, and an indexer cannot be read without an argument.
2. If not found, looks for the first public instance **field** of type `TOut`.
3. If found, stores the value in Memory under `typeof(TOut)`.
4. If not found or null, sets the exception: `"Could not find non-null value of type: ({TOut}) in properties or fields for ({TIn}). Is it public?"`

## Remarks

- Only the **first** matching property/field is extracted. If `TIn` has multiple properties of type `TOut`, only the first one found is used.
- Properties and fields must be **public** and belong to the instance to be discoverable.
- If `TIn` is not found in Memory (parameterless overload), sets the exception: `"Could not find type: ({TIn})."`. That holds for a value type too.
