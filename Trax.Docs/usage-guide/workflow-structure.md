---
layout: default
title: Workflow Structure
parent: Usage Guide
nav_order: 2
---

# Workflow Structure

As your application grows, you'll want a consistent way to organize workflows. Trax.Core recommends grouping each workflow with its input, interface, and steps in a single folder:

```
Workflows/
├── CreateUser/
│   ├── CreateUserRequest.cs        # Input model
│   ├── ICreateUserWorkflow.cs      # Interface
│   ├── CreateUserWorkflow.cs       # Implementation
│   └── Steps/
│       ├── ValidateEmailStep.cs
│       ├── CreateUserInDatabaseStep.cs
│       └── SendWelcomeEmailStep.cs
│
├── ProcessOrder/
│   ├── ProcessOrderRequest.cs
│   ├── IProcessOrderWorkflow.cs
│   ├── ProcessOrderWorkflow.cs
│   └── Steps/
│       ├── ValidateOrderStep.cs
│       ├── ChargePaymentStep.cs
│       └── CreateShipmentStep.cs
```

This structure keeps everything related to a workflow in one place. When you need to modify `CreateUser`, you know exactly where to look.

## The Input Model

Each workflow gets its own request type. This is required by the `WorkflowBus`—input types must be unique across your application:

```csharp
namespace YourApp.Workflows.CreateUser;

public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
```

Keep the request in the same namespace as the workflow. This makes imports cleaner and signals that the type belongs to this workflow.

## The Interface

Define an interface for DI resolution and testing:

```csharp
namespace YourApp.Workflows.CreateUser;

public interface ICreateUserWorkflow : IServiceTrain<CreateUserRequest, User>;
```

One line. The interface exists for dependency injection and to make the workflow's contract explicit. When you see `ICreateUserWorkflow`, you immediately know it takes `CreateUserRequest` and returns `User`.

## The Implementation

The workflow class lives alongside its interface:

```csharp
namespace YourApp.Workflows.CreateUser;

public class CreateUserWorkflow : ServiceTrain<CreateUserRequest, User>, ICreateUserWorkflow
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateEmailStep>()
            .Chain<CreateUserInDatabaseStep>()
            .Chain<SendWelcomeEmailStep>()
            .Resolve();
}
```

The implementation reads like a table of contents for the workflow. Someone unfamiliar with the code can see the high-level flow without digging into step implementations.

## The Steps Folder

Steps go in a `Steps/` subfolder. Mark them `internal`—they're implementation details of this workflow:

```csharp
namespace YourApp.Workflows.CreateUser.Steps;

internal class ValidateEmailStep(IUserRepository userRepository) : Step<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        var existing = await userRepository.GetByEmailAsync(input.Email);
        if (existing != null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}
```

Using `internal` keeps your public API surface clean. External code interacts with `ICreateUserWorkflow`, not individual steps.

## When to Share Steps

Sometimes multiple workflows need the same validation or transformation. Resist the urge to share steps too early—duplication is often cheaper than the wrong abstraction.

When you do need to share, create a `Shared/` folder at the `Workflows/` level:

```
Workflows/
├── Shared/
│   └── Steps/
│       └── ValidateEmailFormatStep.cs
├── CreateUser/
│   └── ...
├── UpdateUser/
│   └── ...
```

Shared steps should be truly generic. If you find yourself adding conditionals to handle different workflows, that's a sign the step should be duplicated and specialized instead.
