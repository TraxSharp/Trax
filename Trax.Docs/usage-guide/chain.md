---
layout: default
title: Chain
parent: Usage Guide
nav_order: 3
---

# Chain

`.Chain<TStep>()` is the primary way to add a step to a workflow. It resolves the step from the DI container, pulls its input from [Memory](../concepts/memory.md), runs it, and stores the output back in Memory.

```csharp
Activate(input)
    .Chain<ValidateEmailStep>()
    .Chain<CreateUserStep>()
    .Chain<SendEmailStep>()
    .Resolve();
```

*API Reference: [Activate]({{ site.baseurl }}{% link api-reference/workflow-methods/activate.md %}), [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

For all 12 overloads, type parameter constraints, and step-wiring behavior, see [API Reference: Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}). The [Analyzer](../analyzer.md) catches missing types at compile time, so you'll see these errors in your IDE before you ever run the code.

## Railway Behavior

If a previous step threw an exception, `.Chain<TStep>()` is skipped entirely. The exception propagates through the chain until it reaches `.Resolve()`, which returns it as `Left(exception)`.

```csharp
Activate(input)
    .Chain<ValidateEmailStep>()    // Throws ValidationException
    .Chain<CreateUserStep>()       // Skipped
    .Chain<SendEmailStep>()        // Skipped
    .Resolve();                    // Returns Left(ValidationException)
```

*API Reference: [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

This is the core of Railway Oriented Programming—see [Railway Programming](../concepts/railway-programming.md) for the full explanation.

