---
layout: default
title: Mediator
parent: Usage Guide
nav_order: 8
---

# Mediator & TrainBus

The `Trax.Mediator` package provides the `TrainBus` — the dispatch station that routes trains by their input type instead of requiring you to inject each one directly.

## The Mediator Pattern

Normally, if a controller needs to run a train, it injects the train directly:

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

This works, but the controller needs to know about every train it calls. The mediator pattern replaces those direct dependencies with a single dispatch point:

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

*SDK Reference: [TrainBus.RunAsync]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-bus.md %})*

The `TrainBus` looks at the input type (`CreateUserRequest`), finds the train registered for that type, and dispatches it. The controller doesn't need to know which train class handles the request — it just drops off the cargo and gets back a delivery.

## Setup

```bash
dotnet add package Trax.Mediator
```

Register trains during startup by pointing the bus at your assemblies:

```csharp
builder.Services.AddTrax.CoreEffects(options => options
    .AddServiceTrainBus(typeof(Program).Assembly)
);
```

*SDK Reference: [AddServiceTrainBus]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-train-bus.md %})*

Pass multiple assemblies if your trains live in different projects. Each input type must map to exactly one train—see [SDK Reference: AddServiceTrainBus]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-train-bus.md %}) for discovery mechanics, input type uniqueness rules, and lifetime considerations.

## Using TrainBus in a Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController(ITrainBus trainBus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
    {
        try
        {
            var order = await trainBus.RunAsync<Order>(request);
            return Ok(order);
        }
        catch (TrainException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

*SDK Reference: [TrainBus.RunAsync]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-bus.md %})*

## Nested Trains

Steps and trains can run other trains through the `TrainBus`. Pass the current `Metadata` to `RunAsync` to establish a parent-child relationship—the parent's `Metadata.Id` becomes the child's `ParentId`, creating a tree you can query to trace execution across trains.

See [SDK Reference: TrainBus.RunAsync]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-bus.md %}) for the full nested train example and all method signatures.

## Direct Injection

You don't have to use the bus. `AddServiceTrainBus` also registers each train by its interface, so you can inject them directly:

```csharp
// GraphQL mutation using Hot Chocolate
[ExtendObjectType("Mutation")]
public class UpdateUserMutation : IGqlMutation
{
    public async Task<UpdateUserOutput> UpdateUser(
        [Service] IUpdateUserTrain updateUserTrain,
        UpdateUserInput input
    ) => await updateUserTrain.Run(input);
}
```

Use direct injection when:
- You know exactly which train you need at compile time
- You want stronger type signatures
- You prefer explicit dependencies over the mediator pattern

The train still gets all the same effect handling (metadata, persistence, etc.)—it's just resolved differently.

## Programmatic Execution with ITrainExecutionService

`ITrainExecutionService` handles the common pattern of "run or queue a train by name with JSON input." It's registered automatically by `AddServiceTrainBus()` and used internally by the [REST]({{ site.baseurl }}{% link sdk-reference/rest-api.md %}) and [GraphQL]({{ site.baseurl }}{% link sdk-reference/graphql-api.md %}) APIs.

### Queue for Scheduler Dispatch

`QueueAsync` creates a `WorkQueue` entry. The scheduler picks it up on its next poll cycle and dispatches it on whatever machine runs the scheduler.

```csharp
public class JobController(ITrainExecutionService executionService) : ControllerBase
{
    [HttpPost("queue")]
    public async Task<IActionResult> Queue(string trainName, [FromBody] JsonElement input)
    {
        var result = await executionService.QueueAsync(trainName, input.GetRawText(), priority: 10);
        return Ok(new { result.WorkQueueId, result.ExternalId });
    }
}
```

### Run Directly

`RunAsync` calls `ITrainBus.RunAsync` on the current machine. The request blocks until the train completes.

```csharp
[HttpPost("run")]
public async Task<IActionResult> Run(string trainName, [FromBody] JsonElement input, CancellationToken ct)
{
    var result = await executionService.RunAsync(trainName, input.GetRawText(), ct);
    return Ok(new { result.MetadataId });
}
```

Both methods look up the train by its fully qualified service type name (e.g., `Trax.Samples.Api.Rest.Trains.Greet.IGreetTrain`), deserialize the JSON to the correct input type, and handle the execution. If the train name doesn't match any registered train, an `InvalidOperationException` is thrown.

*SDK Reference: [TrainExecution]({{ site.baseurl }}{% link sdk-reference/mediator-api/train-execution.md %})*
