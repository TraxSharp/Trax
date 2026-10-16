---
title: Organize trains in a project
description: "Lay out a project's trains one folder each: the input and output, the interface and class, the junctions, the models, and where shared junctions go."
type: how-to
area: trains
nav_order: 2
---

# Organize trains in a project

Give each train a folder. Its top level holds the train's contract and nothing else: the input,
the output, the interface and the class. The junctions go in `Junctions/`, and every other type
the train uses goes in `Models/`:

```text
Trains/
├── CreateUser/
│   ├── CreateUserRequest.cs        # input
│   ├── ICreateUserTrain.cs         # interface
│   ├── CreateUserTrain.cs          # train
│   └── Junctions/
│       ├── ValidateEmailJunction.cs
│       └── CreateUserInDatabaseJunction.cs
└── ProcessOrder/
    ├── ProcessOrderRequest.cs      # input
    ├── ProcessOrderResult.cs       # output
    ├── IProcessOrderTrain.cs
    ├── ProcessOrderTrain.cs
    ├── Junctions/
    │   ├── PriceOrderJunction.cs
    │   └── ChargePaymentJunction.cs
    └── Models/
        ├── OrderLine.cs            # part of the input: public
        └── PricedOrder.cs          # passed between junctions: internal
```

## 1. Give the train its own input type

```csharp compile=createuser
namespace YourApp.Trains.CreateUser;

public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
```

`ITrainBus.RunAsync(input)` finds the train by the input's type. Two trains may share an input type,
but the bus then reaches only the first one it scanned, and the other can be run only by name. A
type per train keeps every train reachable by input.

## 2. Declare an interface for the train

```csharp compile=createuser
using Trax.Effect.Services.ServiceTrain;

namespace YourApp.Trains.CreateUser;

public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;
```

`AddMediator` registers the train under this interface, callers inject it, and its full name
(`YourApp.Trains.CreateUser.ICreateUserTrain`) is the name runs are recorded under.

## 3. Put the junctions in a Junctions folder, internal

```csharp compile=createuser
using System.ComponentModel.DataAnnotations;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace YourApp.Trains.CreateUser.Junctions;

internal class ValidateEmailJunction(IUserRepository users)
    : EffectJunction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (await users.GetByEmailAsync(input.Email) is not null)
            throw new ValidationException($"Email {input.Email} already exists");

        return Unit.Default;
    }
}

internal class CreateUserInDatabaseJunction(IUserRepository users)
    : EffectJunction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        users.CreateAsync(new User(input.Email, $"{input.FirstName} {input.LastName}"));
}
```

The rest of the application talks to `ICreateUserTrain`, so the junctions stay `internal`. Take
dependencies through the constructor. Do not put `[Inject]` properties on a junction: a junction the
chain builds with `Chain<T>()` is built from its constructor and its properties are never filled
(the [startup chain check](/docs/core/startup-chain-check#warnings) warns about each one).
`[Inject]` is for the train's own properties.

## 4. Write the train

```csharp compile=createuser
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using YourApp.Trains.CreateUser.Junctions;

namespace YourApp.Trains.CreateUser;

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>().Chain<CreateUserInDatabaseJunction>().Resolve();
}
```

## 5. Put the other types in Models, internal unless a caller needs them

A type the input or output is made of is part of the contract, so callers build or read it. It goes
in `Models/` and stays `public`:

```csharp compile=processorder
namespace YourApp.Trains.ProcessOrder.Models;

public record OrderLine(string Sku, int Quantity);
```

A type only the junctions hand each other, through Memory, is part of how the train works. It goes
in `Models/` too, and is `internal`, like the junctions that use it:

```csharp compile=processorder
namespace YourApp.Trains.ProcessOrder.Models;

internal record PricedOrder(decimal Total);
```

Memory finds an `internal` type the same way it finds a public one, and the
[startup chain check](/docs/core/startup-chain-check) checks it the same way. Keeping these
types `internal` means the compiler, not the folder, stops the rest of the application from
depending on a train's insides. A type that more than one train uses belongs to neither: give it a
folder of its own outside `Trains/`.

## 6. Share a junction only when it is generic

When several trains need the same junction, put it in `Trains/Shared/Junctions/`:

```text
Trains/
├── Shared/
│   └── Junctions/
│       └── ValidateEmailFormatJunction.cs
├── CreateUser/
└── UpdateUser/
```

Share late. A shared junction that grows conditionals for the trains that use it should be split
back into one junction per train.

## SDK Reference

> [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) | [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) | [Chain](/docs/sdk-reference/train-methods/chain) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [TrainBus](/docs/sdk-reference/mediator-api/i-train-bus)
