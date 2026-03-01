---
layout: default
title: Mediator
parent: Usage Guide
nav_order: 8
---

# Mediator & WorkflowBus

The `Trax.Mediator` package provides the `WorkflowBus`—a way to dispatch workflows by their input type instead of injecting each one directly.

## The Mediator Pattern

Normally, if a controller needs to run a workflow, it injects the workflow directly:

```csharp
public class UsersController(ICreateUserWorkflow createUserWorkflow) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var user = await createUserWorkflow.Run(request);
        return Ok(user);
    }
}
```

This works, but the controller needs to know about every workflow it calls. The mediator pattern replaces those direct dependencies with a single dispatch point:

```csharp
public class UsersController(IWorkflowBus workflowBus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var user = await workflowBus.RunAsync<User>(request);
        return Ok(user);
    }
}
```

*API Reference: [WorkflowBus.RunAsync]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %})*

The `WorkflowBus` looks at the input type (`CreateUserRequest`), finds the workflow registered for that type, and runs it. The controller doesn't need to know which workflow class handles the request—it just sends the input and gets back a result.

## Setup

```bash
dotnet add package Trax.Mediator
```

Register workflows during startup by pointing the bus at your assemblies:

```csharp
builder.Services.AddTrax.CoreEffects(options => options
    .AddServiceTrainBus(typeof(Program).Assembly)
);
```

*API Reference: [AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/configuration/add-effect-workflow-bus.md %})*

Pass multiple assemblies if your workflows live in different projects. Each input type must map to exactly one workflow—see [API Reference: AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/configuration/add-effect-workflow-bus.md %}) for discovery mechanics, input type uniqueness rules, and lifetime considerations.

## Using WorkflowBus in a Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController(IWorkflowBus workflowBus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
    {
        try
        {
            var order = await workflowBus.RunAsync<Order>(request);
            return Ok(order);
        }
        catch (WorkflowException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

*API Reference: [WorkflowBus.RunAsync]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %})*

## Nested Workflows

Steps and workflows can run other workflows through the `WorkflowBus`. Pass the current `Metadata` to `RunAsync` to establish a parent-child relationship—the parent's `Metadata.Id` becomes the child's `ParentId`, creating a tree you can query to trace execution across workflows.

See [API Reference: WorkflowBus.RunAsync]({{ site.baseurl }}{% link api-reference/mediator-api/workflow-bus.md %}) for the full nested workflow example and all method signatures.

## Direct Injection

You don't have to use the bus. `AddServiceTrainBus` also registers each workflow by its interface, so you can inject them directly:

```csharp
// GraphQL mutation using Hot Chocolate
[ExtendObjectType("Mutation")]
public class UpdateUserMutation : IGqlMutation
{
    public async Task<UpdateUserOutput> UpdateUser(
        [Service] IUpdateUserWorkflow updateUserWorkflow,
        UpdateUserInput input
    ) => await updateUserWorkflow.Run(input);
}
```

Use direct injection when:
- You know exactly which workflow you need at compile time
- You want stronger type signatures
- You prefer explicit dependencies over the mediator pattern

The workflow still gets all the same effect handling (metadata, persistence, etc.)—it's just resolved differently.
