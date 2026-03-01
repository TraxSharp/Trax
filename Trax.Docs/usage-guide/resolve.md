---
layout: default
title: Resolve
parent: Usage Guide
nav_order: 4
---

# Resolve

`.Resolve()` terminates the chain and returns `Either<Exception, TReturn>`. Every workflow's `RunInternal` ends with a call to `Resolve`.

```csharp
protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
    => Activate(input)
        .Chain<ValidateEmailStep>()
        .Chain<CreateUserStep>()
        .Chain<SendEmailStep>()
        .Resolve();
```

*API Reference: [Activate]({{ site.baseurl }}{% link api-reference/workflow-methods/activate.md %}), [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

## How It Works

`Resolve` checks for a captured exception, then a [ShortCircuit](short-circuit.md) value, then looks up `TReturn` in [Memory](../concepts/memory.md)—in that order. See [API Reference: Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %}) for the full resolution priority and error behavior.

The [Analyzer](../analyzer.md) catches missing return types at compile time with **CHAIN002**.

## The Parameterized Overload

There's a second overload that takes an `Either<Exception, TReturn>` directly:

```csharp
protected override async Task<Either<Exception, ParentResult>> RunInternal(ParentRequest input)
{
    var childResult = await WorkflowBus.RunAsync<ChildResult>(
        new ChildRequest { Data = input.ChildData },
        Metadata
    );

    return Activate(input)
        .Chain<ValidateStep>()
        .Resolve(new ParentResult
        {
            ParentData = input.ParentData,
            ChildResult = childResult
        });
}
```

*API Reference: [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %}), [WorkflowBus.RunAsync]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %})*

This skips the Memory lookup—you're providing the result directly. If an exception exists from the chain, it still takes precedence and the provided value is ignored. This is useful when you need to construct the return value manually, like combining results from nested workflows with the chain's output.

