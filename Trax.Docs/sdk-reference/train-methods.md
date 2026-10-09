---
layout: default
title: Train Methods
description: "Index of the methods on Train for composing junctions inside Junctions(): Chain, ShortCircuit, Extract, AddServices, Resolve, Run and DeclaredChain."
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
| [Decide](/docs/sdk-reference/train-methods/decide) | Asks a decider several typed questions about a value in Memory, in one call, and stores the typed decisions |
| [Switch](/docs/sdk-reference/train-methods/switch) | Sends the train down one of several declared tracks, chosen by a decision |
| [Parallel](/docs/sdk-reference/train-methods/parallel) | Runs a fixed set of named branches side by side, each on its own copy of Memory, and joins them (experimental) |
| [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | Stores a state the chain declares, so a later run of the same input resumes after it |
| [Gate](/docs/sdk-reference/train-methods/gate) | Routes on the probability that a yes/no answer is yes |
| [Scale](/docs/sdk-reference/train-methods/scale) | Routes on where the state falls on an ordered scale |
| [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | Reads the declared chain without running it, and verifies it with `ChainVerification.Verify` |
| [ChainGraph](/docs/sdk-reference/train-methods/chain-graph) | The declared chain as a graph with stable node ids, canonical JSON and a hash, and the id of the step a run is executing |
