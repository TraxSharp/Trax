---
layout: default
title: Mediator
nav_order: 5
has_children: true
section: Packages
---

# Trax.Mediator

`Trax.Mediator` provides the `TrainBus`, a dispatch station that routes trains by their input type instead of requiring direct injection.

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

Each input type maps to exactly one train. If two trains accept the same `TIn`, the first registration wins and the duplicate is silently skipped. This means `TrainBus.RunAsync` always resolves to a single train for a given input type. If you need multiple trains that share an input type, inject them directly by interface instead of dispatching through the bus.

## Nested Trains

A junction can dispatch another train through the `TrainBus`. Inject `ITrainBus` into the junction and pass the input, plus the junction's `CancellationToken` so cancelling the parent reaches the child:

```csharp
var result = await trainBus.RunAsync<ChildResult>(
    new ChildRequest { Data = input.ChildData },
    CancellationToken
);
```

The child runs as a train of its own, with its own metadata record, and that record is **not linked to the parent**: nothing sets its `ParentId`. The optional `Metadata` argument to `RunAsync` is not a parent link. It is a pre-created `Pending` record for the train to run *as*, the way the scheduler uses it, and passing the running parent's `Metadata` throws a `TrainException`. To trace a child back to its parent today, correlate them yourself, for example by carrying the parent's `ExternalId` in the child's input.

## Scope Isolation

Every `RunAsync` call creates its own DI scope. The train is resolved and executed within this scope, which is disposed when the call returns. This is especially important for Blazor Server where the circuit scope persists for the entire connection. Without per-call scoping, trains would share `DbContext` instances and other scoped resources across executions.

Nested dispatch (a train calling `TrainBus.RunAsync` for a child train) also creates a separate scope. Each train is a self-contained black box.

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

When you need recurring background jobs, add [Trax.Scheduler](/docs/scheduler).

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus)
