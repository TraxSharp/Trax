---
layout: default
title: Workflow Methods
parent: API Reference
nav_order: 1
has_children: true
---

# Workflow Methods

Methods available on `Train<TInput, TReturn>` for composing steps inside `RunInternal`. These are the core building blocks of every Trax.Core workflow pipeline.

A typical workflow calls these methods in sequence:

```csharp
protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input)
{
    return Activate(input)              // Store input in Memory
        .Chain<ValidateOrder>()         // Execute step, auto-wiring input/output via Memory
        .Chain<ProcessPayment>()
        .Chain<SendConfirmation>()
        .Resolve();                     // Extract the final result from Memory
}
```

| Method | Description |
|--------|-------------|
| [Activate]({{ site.baseurl }}{% link api-reference/workflow-methods/activate.md %}) | Stores the workflow input (and optional extra objects) into Memory |
| [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}) | Executes a step, wiring its input from Memory and storing its output back |
| [ShortCircuit]({{ site.baseurl }}{% link api-reference/workflow-methods/short-circuit.md %}) | Executes a step that can return early, bypassing remaining steps |
| [Extract]({{ site.baseurl }}{% link api-reference/workflow-methods/extract.md %}) | Pulls a nested property/field out of a Memory object into its own Memory slot |
| [AddServices]({{ site.baseurl }}{% link api-reference/workflow-methods/add-services.md %}) | Stores DI services into Memory so steps can access them |
| [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %}) | Extracts the final `TReturn` result from Memory (last call in `RunInternal`) |
| [Run / RunEither]({{ site.baseurl }}{% link api-reference/workflow-methods/run.md %}) | Executes the workflow from the outside — `Run` throws on failure, `RunEither` returns `Either` |
