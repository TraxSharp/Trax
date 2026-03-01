---
layout: default
title: Activate
parent: Workflow Methods
grand_parent: API Reference
nav_order: 1
---

# Activate

Stores the workflow input (and optional extra objects) into Memory. Typically the **first** method called in `RunInternal`. After activation, the input is available to subsequent steps via Memory's type-based lookup.

## Signature

```csharp
public Train<TInput, TReturn> Activate(TInput input, params object[] otherInputs)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `TInput` | Yes | The primary workflow input. Stored in Memory by its concrete type and all its interfaces. |
| `otherInputs` | `params object[]` | No | Additional objects to store in Memory. Each is stored by its concrete type and interfaces. |

## Returns

`Train<TInput, TReturn>` — the workflow instance, for fluent chaining.

## Examples

### Basic Activation

```csharp
protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input)
{
    return Activate(input)
        .Chain<ValidateOrder>()
        .Chain<ProcessPayment>()
        .Resolve();
}
```

### With Extra Inputs

```csharp
protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input)
{
    return Activate(input, _configService, _logger)
        .Chain<ValidateOrder>()
        .Resolve();
}
```

## Behavior Details

### Simple Types
The object is stored by its concrete type **and** all its interfaces. For example, if `input` is of type `OrderInput` which implements `IOrderInput`, it's stored under both `typeof(OrderInput)` and `typeof(IOrderInput)`.

### Tuple Types
Each element of the tuple is extracted and stored individually. For example, `(string, int)` stores the `string` and `int` as separate Memory entries.

### Null Input
If `input` is `null`, the workflow's exception is set to `"Input ({typeof(TInput)}) is null."` and subsequent steps are short-circuited.

## Remarks

- Memory is initialized with `Unit.Default` under `typeof(Unit)`, allowing parameterless step invocations.
- The `otherInputs` parameter follows the same tuple/interface storage rules as the primary input.
- See [Memory]({{ site.baseurl }}{% link concepts/memory.md %}) for details on how the type-keyed dictionary works.
