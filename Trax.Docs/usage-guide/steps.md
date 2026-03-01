---
layout: default
title: Steps
parent: Usage Guide
nav_order: 1
---

# Steps

Steps are the building blocks of workflows. Each step does one thing:

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

Steps use constructor injection for dependencies. When a step throws, the workflow stops and returns the exception.

## CancellationToken in Steps

Every step has a `CancellationToken` property that is set automatically by the workflow before `Run()` is called. Use it to pass cancellation to async operations:

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

The token comes from the caller: `workflow.Run(input, cancellationToken)`. If no token is provided, `CancellationToken` defaults to `CancellationToken.None`. Before each step executes, cancellation is checked — if the token is already cancelled, the step is skipped and `OperationCanceledException` propagates.

*Full details: [Cancellation Tokens]({{ site.baseurl }}{% link usage-guide/cancellation-tokens.md %})*

## EffectStep vs Step

Trax.Core has two step base classes:

**`Step<TIn, TOut>`** — The base class. Handles input/output and Railway error propagation. No metadata, no lifecycle hooks. Use this for lightweight steps or when running inside a plain `Train`.

**`EffectStep<TIn, TOut>`** — Extends `Step` with per-step metadata tracking. When run inside a `ServiceTrain`, it records a `StepMetadata` entry with the step's name, input/output types, start/end times, and Railway state. Step effect providers (like `AddStepLogger`) hook into `EffectStep`'s lifecycle—they fire before and after each step executes.

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

The implementation is identical—just swap the base class. `EffectStep` only adds metadata when running inside a `ServiceTrain`. If you use `EffectStep` inside a plain `Train`, it throws at runtime.

Use `EffectStep` when you want step-level observability (timing, logging via `AddStepLogger`). Use `Step` when you don't need it.

*API Reference: [AddStepLogger]({{ site.baseurl }}{% link api-reference/configuration/add-step-logger.md %})*

## Dependency Injection in Steps

Steps use standard constructor injection for their dependencies. Do **not** use the `[Inject]` attribute—that's used internally by the `ServiceTrain` base class for its own framework-level services.

```csharp
// ❌ Don't use [Inject] in your steps
public class MyStep : Step<Input, Output>
{
    [Inject]
    public IMyService MyService { get; set; }
}

// ✅ Use constructor injection
public class MyStep(IMyService MyService) : Step<Input, Output>
{
    public override async Task<Output> Run(Input input)
    {
        var result = await MyService.DoSomethingAsync(input);
        return result;
    }
}
```
