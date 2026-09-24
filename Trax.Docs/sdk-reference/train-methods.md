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
protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>()              // Execute junction, auto-wiring input/output via Memory
        .Chain<ProcessPayment>()
        .Chain<SendConfirmation>().Resolve();     // Final result extracted from Memory automatically
```

`Junctions()` is the only way to declare a chain. There is no imperative alternative: a chain
assembled in code has no single shape, so a host could not check it before serving traffic. Work
that used to sit above the chain belongs in a junction at the head of it.

| Method | Description |
|--------|-------------|
| [Junctions](/docs/sdk-reference/train-methods/junctions) | Override to define the train's route, the primary way to compose junctions |
| [Chain](/docs/sdk-reference/train-methods/chain) | Executes a junction, wiring its input from Memory and storing its output back |
| [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) | Executes a junction whose `Right` value becomes the train's result; later junctions still run |
| [Extract](/docs/sdk-reference/train-methods/extract) | Pulls a nested property/field out of a Memory object into its own Memory slot |
| [AddServices](/docs/sdk-reference/train-methods/add-services) | Stores DI services into Memory so junctions can access them |
| [Resolve](/docs/sdk-reference/train-methods/resolve) | Ends the chain, taking the `TReturn` result out of Memory |
| [Run / RunEither](/docs/sdk-reference/train-methods/run) | Executes the train from the outside. `Run` throws on failure, `RunEither` returns `Either` |
| [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | Reads the declared chain without running it, and verifies it with `ChainVerification.Verify` |
