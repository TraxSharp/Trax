---
layout: default
title: Extract
parent: Usage Guide
nav_order: 6
---

# Extract

`.Extract<TSource, TTarget>()` pulls a nested value out of an object in [Memory](../concepts/memory.md). It finds the `TSource` object, looks for a property or field of type `TTarget`, and stores that value in Memory under the `TTarget` type.

```csharp
Activate(input)
    .Chain<LoadUserStep>()              // Returns User, stored in Memory
    .Extract<User, EmailAddress>()      // Finds EmailAddress property on User, stores it
    .Chain<ValidateEmailStep>()         // Takes EmailAddress from Memory
    .Resolve();
```

*API Reference: [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [Extract]({{ site.baseurl }}{% link api-reference/workflow-methods/extract.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

`Extract` uses reflection to find a property or field on `TSource` whose type matches `TTarget` and stores it in Memory. See [API Reference: Extract]({{ site.baseurl }}{% link api-reference/workflow-methods/extract.md %}) for the full search order and failure behavior.

## When to Use It

`Extract` is a convenience for avoiding a step that exists solely to pull a property off an object. Without it, you'd write:

```csharp
public class GetUserEmailStep : Step<User, EmailAddress>
{
    public override Task<EmailAddress> Run(User input)
        => Task.FromResult(input.Email);
}
```

`.Extract<User, EmailAddress>()` does the same thing without the boilerplate.

Use it when the property access is trivial. If you need any logic (null checking, transformation, validation), write a step instead.

