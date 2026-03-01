---
layout: default
title: AddServices
parent: Usage Guide
nav_order: 7
---

# AddServices

`.AddServices()` puts service instances directly into [Memory](../concepts/memory.md), making them available to subsequent steps. This bypasses the DI container—the instances you pass are stored as-is.

```csharp
protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
{
    var validator = new CustomValidator();
    var notifier = new SlackNotifier();

    return Activate(input)
        .AddServices<IValidator, INotifier>(validator, notifier)
        .Chain<ValidateStep>()     // Can take IValidator from Memory
        .Chain<CreateUserStep>()
        .Chain<NotifyStep>()       // Can take INotifier from Memory
        .Resolve();
}
```

*API Reference: [Activate]({{ site.baseurl }}{% link api-reference/workflow-methods/activate.md %}), [AddServices]({{ site.baseurl }}{% link api-reference/workflow-methods/add-services.md %}), [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

Each type argument is stored in Memory with the corresponding instance. See [API Reference: AddServices]({{ site.baseurl }}{% link api-reference/workflow-methods/add-services.md %}) for all overloads and interface-type storage behavior.

## When to Use It

Use `AddServices` when you need to inject runtime-created instances into the chain—objects that aren't available through the DI container or that need to be created per-execution.

For standard dependencies, prefer constructor injection in your steps instead.

