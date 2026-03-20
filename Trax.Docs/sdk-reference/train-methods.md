---
layout: default
title: Train Methods
parent: SDK Reference
nav_order: 1
has_children: true
---

# Train Methods

Methods available on `Train<TInput, TReturn>` for composing junctions inside `RunInternal`. These are the core building blocks of every Trax.Core train pipeline.

A typical train calls these methods in sequence:

```csharp
protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input)
{
    return Activate(input)              // Store input in Memory
        .Chain<ValidateOrder>()         // Execute junction, auto-wiring input/output via Memory
        .Chain<ProcessPayment>()
        .Chain<SendConfirmation>()
        .Resolve();                     // Extract the final result from Memory
}
```

| Method | Description |
|--------|-------------|
| [Activate](/docs/sdk-reference/train-methods/activate) | Stores the train input (and optional extra objects) into Memory |
| [Chain](/docs/sdk-reference/train-methods/chain) | Executes a junction, wiring its input from Memory and storing its output back |
| [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) | Executes a junction that can return early, bypassing remaining junctions |
| [Extract](/docs/sdk-reference/train-methods/extract) | Pulls a nested property/field out of a Memory object into its own Memory slot |
| [AddServices](/docs/sdk-reference/train-methods/add-services) | Stores DI services into Memory so junctions can access them |
| [Resolve](/docs/sdk-reference/train-methods/resolve) | Extracts the final `TReturn` result from Memory (last call in `RunInternal`) |
| [Run / RunEither](/docs/sdk-reference/train-methods/run) | Executes the train from the outside — `Run` throws on failure, `RunEither` returns `Either` |
