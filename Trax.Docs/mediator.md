---
title: Calling a train by its input (Trax.Mediator)
description: "Trax.Mediator and the train bus: dispatching a train by its input type instead of injecting it, nested trains, and the scope each run gets."
type: explanation
area: mediator
nav_order: 1
source:
  - Trax.Mediator/src/Trax.Mediator/Services/TrainBus
  - Trax.Mediator/src/Trax.Mediator/Services/TrainRegistry/TrainRegistry.cs
---

# Calling a train by its input (Trax.Mediator)

A controller, a GraphQL mutation, the scheduler and a worker all need to start the same trains,
and none of them should have to reference each train's class.
`Trax.Mediator` provides `ITrainBus`, which runs a train chosen by the type of the input it is
given. [AddMediator](/docs/sdk-reference/configuration/add-mediator)
registers it, scans the assemblies it is given for trains, and registers each train under its
interface. How the scan and the lookup work in detail is in [Train discovery](/docs/mediator/train-discovery).

## Why dispatch by input type

Without the bus, a caller injects each train it calls:

```csharp compile=injected
using Microsoft.AspNetCore.Mvc;

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

That works, and a caller that knows exactly which train it needs can keep doing it: `AddMediator`
registers every train it finds by its interface. The cost is that the caller's constructor grows
with every train it calls. With the bus, it takes one dependency:

```csharp compile=bus
using Microsoft.AspNetCore.Mvc;
using Trax.Mediator.Services.TrainBus;

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

`RunAsync` looks up the train registered for `CreateUserRequest` and runs it. The input type is
the address, so one train can run another without referencing it, which is what nested trains
do. The bus also runs a train by name (`RunByNameAsync`), which is how the scheduler's job runner
and, for a train it runs in-process, `ITrainExecutionService` (behind the GraphQL API and the
dashboard) start a train they know only by name.

An input type reaches one train through the bus. When two trains take the same input type, both
are registered and discovered, and `RunAsync` runs the one found first in the scan. Everything
that names a train runs the one it names: `ITrainBus.RunByNameAsync`, `ITrainExecutionService`,
and the GraphQL `run` operations. To run a specific train from code when its input type is
shared, call `RunByNameAsync` or inject the train by its interface.

## Nested trains

A junction can run another train through the bus. It injects `ITrainBus` and passes its own
`CancellationToken`, so cancelling the parent reaches the child:

```csharp fragment
var result = await trainBus.RunAsync<ChildResult>(
    new ChildRequest { Data = input.ChildData },
    CancellationToken
);
```

The child runs as a train of its own, with its own metadata record, and that record is **not
linked to the parent**: nothing sets its `ParentId`. The optional `Metadata` argument to
`RunAsync` is not a parent link. It is a pre-created `Pending` record for the train to run *as*,
the way the scheduler uses it, and passing the running parent's `Metadata` throws a
`TrainException`. To trace a child back to its parent, correlate them yourself, for example by
carrying the parent's `ExternalId` in the child's input.

## Each run gets its own scope

Every `RunAsync` and `RunByNameAsync` call creates a DI scope, resolves the train in it, runs the
train, and disposes the scope when the call returns. Two runs never share a `DbContext` or any
other scoped service, even when the caller's own scope lives long: a Blazor Server circuit keeps
one scope for the whole connection, and without a scope per run every train started from it would
share that circuit's services. A nested train gets a scope of its own in the same way, so each
train is a self-contained unit.

## When the bus is worth it

The bus pays off in an application where many callers start trains, where trains start other
trains, or where callers should not depend on the classes that implement them. A small
application with one caller per train can inject the trains and skip it. Recurring and queued
work is the [scheduler](/docs/scheduler)'s, which runs trains through the same lookup.

## SDK Reference

> [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)
