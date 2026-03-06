---
layout: default
title: Mediator
nav_order: 5
has_children: true
---

# Trax.Mediator

`Trax.Mediator` provides the `TrainBus` — a dispatch station that routes trains by their input type instead of requiring direct injection.

```bash
dotnet add package Trax.Mediator
```

## The Problem

Without the mediator, controllers inject each train directly:

```csharp
public class UsersController(ICreateUserTrain createUserTrain) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var user = await createUserTrain.Run(request);
        return Ok(user);
    }
}
```

This works, but the controller needs to know about every train it calls.

## With TrainBus

Replace direct dependencies with a single dispatch point:

```csharp
public class UsersController(ITrainBus trainBus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var user = await trainBus.RunAsync<User>(request);
        return Ok(user);
    }
}
```

The `TrainBus` looks at the input type (`CreateUserRequest`), finds the train registered for that type, and dispatches it. The controller doesn't need to know which train class handles the request.

## Setup

Register trains during startup by pointing the bus at your assemblies:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(Program).Assembly)
);
```

Each input type must map to exactly one train — duplicates cause a startup exception.

## Nested Trains

Steps and trains can dispatch other trains through the `TrainBus`. Pass the current `Metadata` to link parent and child journeys:

```csharp
var result = await TrainBus.RunAsync<ChildResult>(
    new ChildRequest { Data = input.ChildData },
    Metadata  // Links parent -> child
);
```

This creates a tree of journey logs you can query to trace execution across trains.

## Direct Injection

You don't have to use the bus. `AddMediator` also registers each train by its interface, so you can inject them directly when you know exactly which train you need:

```csharp
public class UpdateUserMutation(IUpdateUserTrain updateUserTrain)
{
    public async Task<User> UpdateUser(UpdateUserInput input)
        => await updateUserTrain.Run(input);
}
```

## When to Use Mediator

- Larger apps where multiple callers trigger trains
- When trains trigger other trains (nested dispatch)
- When you want to decouple callers from train implementations

When you need recurring background jobs, add [Trax.Scheduler](scheduler.md).
