---
layout: default
title: Trains & Steps
parent: Core
nav_order: 1
---

# Trains & Steps

## Steps

Steps are the stops along a train's route — each one does one thing:

```csharp
public class ValidateEmailStep(IUserRepository UserRepository) : Step<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        var existingUser = await UserRepository.GetByEmailAsync(input.Email);
        if (existingUser != null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}

public class CreateUserStep(IUserRepository UserRepository) : Step<CreateUserRequest, User>
{
    public override async Task<User> Run(CreateUserRequest input)
    {
        var user = new User
        {
            Email = input.Email,
            FullName = $"{input.FirstName} {input.LastName}",
            CreatedAt = DateTime.UtcNow
        };

        return await UserRepository.CreateAsync(user);
    }
}
```

Steps use constructor injection for dependencies. When a step throws, the train derails and returns the exception.

## CancellationToken in Steps

Every step has a `CancellationToken` property that is set automatically by the train before `Run()` is called. Use it to pass cancellation to async operations:

```csharp
public class FetchUserStep(IHttpClientFactory httpFactory) : Step<UserId, UserProfile>
{
    public override async Task<UserProfile> Run(UserId input)
    {
        var client = httpFactory.CreateClient();
        var response = await client.GetAsync($"/users/{input.Value}", CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfile>(CancellationToken);
    }
}
```

The token comes from the caller: `train.Run(input, cancellationToken)`. If no token is provided, `CancellationToken` defaults to `CancellationToken.None`. Before each step executes, cancellation is checked — if the token is already cancelled, the step is skipped and `OperationCanceledException` propagates.

*Full details: [Cancellation Tokens]({{ site.baseurl }}{% link cross-cutting/cancellation-tokens.md %})*

## EffectStep vs Step

Trax has two step base classes:

**`Step<TIn, TOut>`** — The base class. Handles input/output and railway error propagation. No metadata, no lifecycle hooks. Use this for lightweight steps or when running inside a plain `Train`.

**`EffectStep<TIn, TOut>`** — Extends `Step` with per-step metadata tracking. When run inside a `ServiceTrain`, it records a `StepMetadata` entry with the step's name, input/output types, start/end times, and railway state. Step effect providers (like `AddStepLogger`) hook into `EffectStep`'s lifecycle — they fire before and after each step executes.

```csharp
// Base step — no metadata tracking
public class ValidateEmailStep(IUserRepository repo) : Step<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await repo.GetByEmailAsync(input.Email) != null)
            throw new ValidationException("Email already exists");
        return Unit.Default;
    }
}

// Effect step — tracked by step effect providers
public class ValidateEmailStep(IUserRepository repo) : EffectStep<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await repo.GetByEmailAsync(input.Email) != null)
            throw new ValidationException("Email already exists");
        return Unit.Default;
    }
}
```

The implementation is identical — just swap the base class. `EffectStep` only adds metadata when running inside a `ServiceTrain`. If you use `EffectStep` inside a plain `Train`, it throws at runtime.

Use `EffectStep` when you want step-level observability (timing, logging via `AddStepLogger`). Use `Step` when you don't need it.

*SDK Reference: [AddStepLogger]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-logger.md %})*

## Dependency Injection in Steps

Steps use standard constructor injection for their dependencies. Do **not** use the `[Inject]` attribute — that's used internally by the `ServiceTrain` base class for its own framework-level services.

```csharp
// Don't use [Inject] in your steps
public class MyStep : Step<Input, Output>
{
    [Inject]
    public IMyService MyService { get; set; }
}

// Use constructor injection
public class MyStep(IMyService MyService) : Step<Input, Output>
{
    public override async Task<Output> Run(Input input)
    {
        var result = await MyService.DoSomethingAsync(input);
        return result;
    }
}
```

## Train Structure

As your application grows, you'll want a consistent way to organize trains. Group each train with its input, interface, and steps in a single folder:

```
Trains/
├── CreateUser/
│   ├── CreateUserRequest.cs        # Input model
│   ├── ICreateUserTrain.cs         # Interface
│   ├── CreateUserTrain.cs          # Implementation
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

### The Input Model

Each train gets its own request type. This is required by the `TrainBus` — input types must be unique across your application:

```csharp
namespace YourApp.Trains.CreateUser;

public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
```

### The Interface

Define an interface for DI resolution and testing:

```csharp
namespace YourApp.Trains.CreateUser;

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;
```

### The Steps Folder

Steps go in a `Steps/` subfolder. Mark them `internal` — they're implementation details of this train:

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

### When to Share Steps

Sometimes multiple trains need the same validation or transformation. Resist the urge to share steps too early — duplication is often cheaper than the wrong abstraction.

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
