---
layout: default
title: Trains & Junctions
parent: Core
nav_order: 1
---

# Trains & Junctions

## Junctions

Junctions are the points along a train's route — each one does one thing:

```csharp
public class ValidateEmailJunction(IUserRepository UserRepository) : Junction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        var existingUser = await UserRepository.GetByEmailAsync(input.Email);
        if (existingUser != null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}

public class CreateUserJunction(IUserRepository UserRepository) : Junction<CreateUserRequest, User>
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

Junctions use constructor injection for dependencies. When a junction throws, the train switches to the left track and returns the exception.

## CancellationToken in Junctions

Every junction has a `CancellationToken` property that is set automatically by the train before `Run()` is called. Use it to pass cancellation to async operations:

```csharp
public class FetchUserJunction(IHttpClientFactory httpFactory) : Junction<UserId, UserProfile>
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

The token comes from the caller: `train.Run(input, cancellationToken)`. If no token is provided, `CancellationToken` defaults to `CancellationToken.None`. Before each junction executes, cancellation is checked — if the token is already cancelled, the junction is skipped and `OperationCanceledException` propagates.

*Full details: [Cancellation Tokens]({{ site.baseurl }}{% link cross-cutting/cancellation-tokens.md %})*

## EffectJunction vs Junction

Trax has two junction base classes:

**`Junction<TIn, TOut>`** — The base class. Handles input/output and railway error propagation. No metadata, no lifecycle hooks. Use this for lightweight junctions or when running inside a plain `Train`.

**`EffectJunction<TIn, TOut>`** — Extends `Junction` with per-junction metadata tracking. When run inside a `ServiceTrain`, it records a `JunctionMetadata` entry with the junction's name, input/output types, start/end times, and railway state. Junction effect providers (like `AddJunctionLogger`) hook into `EffectJunction`'s lifecycle — they fire before and after each junction executes.

```csharp
// Base junction — no metadata tracking
public class ValidateEmailJunction(IUserRepository repo) : Junction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await repo.GetByEmailAsync(input.Email) != null)
            throw new ValidationException("Email already exists");
        return Unit.Default;
    }
}

// Effect junction — tracked by junction effect providers
public class ValidateEmailJunction(IUserRepository repo) : EffectJunction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await repo.GetByEmailAsync(input.Email) != null)
            throw new ValidationException("Email already exists");
        return Unit.Default;
    }
}
```

The implementation is identical — just swap the base class. `EffectJunction` only adds metadata when running inside a `ServiceTrain`. If you use `EffectJunction` inside a plain `Train`, it throws at runtime.

Use `EffectJunction` when you want junction-level observability (timing, logging via `AddJunctionLogger`). Use `Junction` when you don't need it.

*SDK Reference: [AddJunctionLogger]({{ site.baseurl }}{% link sdk-reference/configuration/add-junction-logger.md %})*

## Dependency Injection in Junctions

Junctions use standard constructor injection for their dependencies. Do **not** use the `[Inject]` attribute — that's used internally by the `ServiceTrain` base class for its own framework-level services.

```csharp
// Don't use [Inject] in your junctions
public class MyJunction : Junction<Input, Output>
{
    [Inject]
    public IMyService MyService { get; set; }
}

// Use constructor injection
public class MyJunction(IMyService MyService) : Junction<Input, Output>
{
    public override async Task<Output> Run(Input input)
    {
        var result = await MyService.DoSomethingAsync(input);
        return result;
    }
}
```

## Train Structure

As your application grows, you'll want a consistent way to organize trains. Group each train with its input, interface, and junctions in a single folder:

```
Trains/
├── CreateUser/
│   ├── CreateUserRequest.cs        # Input model
│   ├── ICreateUserTrain.cs         # Interface
│   ├── CreateUserTrain.cs          # Implementation
│   └── Junctions/
│       ├── ValidateEmailJunction.cs
│       ├── CreateUserInDatabaseJunction.cs
│       └── SendWelcomeEmailJunction.cs
│
├── ProcessOrder/
│   ├── ProcessOrderRequest.cs
│   ├── IProcessOrderTrain.cs
│   ├── ProcessOrderTrain.cs
│   └── Junctions/
│       ├── ValidateOrderJunction.cs
│       ├── ChargePaymentJunction.cs
│       └── CreateShipmentJunction.cs
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

### The Junctions Folder

Junctions go in a `Junctions/` subfolder. Mark them `internal` — they're implementation details of this train:

```csharp
namespace YourApp.Trains.CreateUser.Junctions;

internal class ValidateEmailJunction(IUserRepository userRepository) : Junction<CreateUserRequest, Unit>
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

Using `internal` keeps your public API surface clean. External code interacts with `ICreateUserTrain`, not individual junctions.

### When to Share Junctions

Sometimes multiple trains need the same validation or transformation. Resist the urge to share junctions too early — duplication is often cheaper than the wrong abstraction.

When you do need to share, create a `Shared/` folder at the `Trains/` level:

```
Trains/
├── Shared/
│   └── Junctions/
│       └── ValidateEmailFormatJunction.cs
├── CreateUser/
│   └── ...
├── UpdateUser/
│   └── ...
```

Shared junctions should be truly generic. If you find yourself adding conditionals to handle different trains, that's a sign the junction should be duplicated and specialized instead.
