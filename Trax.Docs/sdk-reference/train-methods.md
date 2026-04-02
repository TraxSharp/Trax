---
layout: default
title: Train Methods
parent: SDK Reference
nav_order: 1
has_children: true
---

# Train Methods

Methods available on `Train<TInput, TReturn>` for composing junctions. These are the core building blocks of every Trax train pipeline.

A typical train overrides `Junctions()` and calls chain methods directly:

```csharp
protected override OrderResult Junctions() =>
    Chain<ValidateOrder>()              // Execute junction, auto-wiring input/output via Memory
        .Chain<ProcessPayment>()
        .Chain<SendConfirmation>();     // Final result extracted from Memory automatically
```

For advanced cases (custom logic, async setup, manual Either construction), override `RunInternal` instead:

```csharp
protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input) =>
    Activate(input)
        .Chain<ValidateOrder>()
        .Chain<ProcessPayment>()
        .Chain<SendConfirmation>()
        .Resolve();
```

| Method | Description |
|--------|-------------|
| [Junctions](/docs/sdk-reference/train-methods/junctions) | Override to define the train's route, the primary way to compose junctions |
| [Chain](/docs/sdk-reference/train-methods/chain) | Executes a junction, wiring its input from Memory and storing its output back |
| [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) | Executes a junction that can return early, bypassing remaining junctions |
| [Extract](/docs/sdk-reference/train-methods/extract) | Pulls a nested property/field out of a Memory object into its own Memory slot |
| [AddServices](/docs/sdk-reference/train-methods/add-services) | Stores DI services into Memory so junctions can access them |
| [Activate](/docs/sdk-reference/train-methods/activate) | Stores the train input into Memory (used in `RunInternal` path) |
| [Resolve](/docs/sdk-reference/train-methods/resolve) | Extracts the final `TReturn` result from Memory (used in `RunInternal` path) |
| [Run / RunEither](/docs/sdk-reference/train-methods/run) | Executes the train from the outside. `Run` throws on failure, `RunEither` returns `Either` |
