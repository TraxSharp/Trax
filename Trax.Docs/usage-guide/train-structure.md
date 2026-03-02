---
layout: default
title: Train Structure
parent: Usage Guide
nav_order: 2
---

# Train Structure

As your application grows, you'll want a consistent way to organize trains. Trax.Core recommends grouping each train with its input, interface, and steps in a single folder:

```
Trains/
├── CreateUser/
│   ├── CreateUserRequest.cs        # Input model
│   ├── ICreateUserTrain.cs      # Interface
│   ├── CreateUserTrain.cs       # Implementation
│   └── Steps/
│       ├── ValidateEmailStep.cs
│       ├── CreateUserInDatabaseStep.cs
│       └── SendWelcomeEmailStep.cs
│
├── ProcessOrder/
│   ├── ProcessOrderRequest.cs
│   ├── IProcessOrderTrain.cs
│   ├── ProcessOrderTrain.cs
│   └── Steps/
│       ├── ValidateOrderStep.cs
│       ├── ChargePaymentStep.cs
│       └── CreateShipmentStep.cs
```

This structure keeps everything related to a train in one place. When you need to modify `CreateUser`, you know exactly where to look.

## The Input Model

Each train gets its own request type. This is required by the `TrainBus`—input types must be unique across your application:

```csharp
namespace YourApp.Trains.CreateUser;

public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
```

Keep the request in the same namespace as the train. This makes imports cleaner and signals that the type belongs to this train.

## The Interface

Define an interface for DI resolution and testing:

```csharp
namespace YourApp.Trains.CreateUser;

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;
```

One line. The interface exists for dependency injection and to make the train's contract explicit. When you see `ICreateUserTrain`, you immediately know it takes `CreateUserRequest` and returns `User`.

## The Implementation

The train class lives alongside its interface:

```csharp
namespace YourApp.Trains.CreateUser;

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateEmailStep>()
            .Chain<CreateUserInDatabaseStep>()
            .Chain<SendWelcomeEmailStep>()
            .Resolve();
}
```

The implementation reads like a table of contents for the train. Someone unfamiliar with the code can see the high-level flow without digging into step implementations.

## The Steps Folder

Steps go in a `Steps/` subfolder. Mark them `internal`—they're implementation details of this train:

```csharp
namespace YourApp.Trains.CreateUser.Steps;

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

Using `internal` keeps your public API surface clean. External code interacts with `ICreateUserTrain`, not individual steps.

## When to Share Steps

Sometimes multiple trains need the same validation or transformation. Resist the urge to share steps too early—duplication is often cheaper than the wrong abstraction.

When you do need to share, create a `Shared/` folder at the `Trains/` level:

```
Trains/
├── Shared/
│   └── Steps/
│       └── ValidateEmailFormatStep.cs
├── CreateUser/
│   └── ...
├── UpdateUser/
│   └── ...
```

Shared steps should be truly generic. If you find yourself adding conditionals to handle different trains, that's a sign the step should be duplicated and specialized instead.
