---
title: Either and Unit
description: "Why Trax.Core has its own Either and Unit types, how a train's result travels as an Either, and the null assertion helpers it ships."
type: explanation
area: trains
nav_order: 4
source:
  - Trax.Core/src/Trax.Core/Functional
  - Trax.Core/src/Trax.Core/Extensions/FunctionalExtensions.cs
---

# Either and Unit

Trax.Core borrows two types from functional programming and ships its own copies of them in the
`Trax.Core.Functional` namespace: `Either` and `Unit`. You do not need a functional programming
background to use either. They exist because a chain of junctions needs a way to carry "this
failed" next to "this is the value", and a way to say "this junction returns nothing" in a generic
type argument.

How the chain uses them, the two tracks a train runs on, is explained in
[The Railway](/docs/core#the-railway). This page is about the types themselves.

## Either

`Either<TLeft, TRight>` holds one of two values. By convention `Left` is the failure and `Right` the
success, and a train's result is an `Either<Exception, TOut>`. That is the type `Junctions()`
returns:

```csharp compile=fp
using Trax.Core.Functional;
using Trax.Core.Train;

public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>().Chain<CreateUserJunction>().Resolve();
}
```

Inside `Junctions()` the `Either` is invisible. You name junctions; if one throws, the chain stops
and the result is `Left(exception)`, and if all of them succeed it is `Right(result)`. Making the
failure a value rather than an exception in flight is what lets the chain skip the remaining
junctions without a `try` around each one, and lets a hook or a host record the failure without
catching it.

The caller usually never sees it either. `Run` unwraps the `Either`: it returns the value on the
right track and rethrows the exception on the left, which is the .NET convention a caller expects.
`RunEither`, on the train class rather than the route interface, hands it back as it is:

```csharp compile=fp
using Trax.Core.Functional;

public static class CreateUser
{
    public static async Task<string> Describe(CreateUserTrain train, CreateUserRequest request)
    {
        Either<Exception, User> result = await train.RunEither(request);

        return result.Match(
            Left: exception => $"Failed: {exception.Message}",
            Right: user => $"Created: {user.Email}"
        );
    }
}
```

`RunEither` lets nothing escape, a cancellation included, which is why a caller of it checks a
`Left` for `OperationCanceledException` before treating it as a business failure. See
[Handle errors in trains](/docs/cross-cutting/handle-errors-in-trains).

A `default(Either)` holds neither value. `IsLeft` and `IsRight` are both false for it, and `Match`
throws unless you pass the optional `Bottom` function.

## Unit

`Unit` is a type with one value, `Unit.Default`. C# cannot write `Task<void>` or use `void` as a
generic argument, so a junction or a train that produces nothing says so with `Unit`:

```csharp compile=fp
using Trax.Core.Functional;
using Trax.Core.Junction;

public class ValidateEmailJunction : Junction<CreateUserRequest, Unit>
{
    public override Task<Unit> Run(CreateUserRequest input)
    {
        if (!input.Email.Contains('@'))
            throw new ArgumentException("Invalid email", nameof(input));

        return Task.FromResult(Unit.Default);
    }
}
```

A junction that returns `Unit` did its work and added nothing to the train's memory. The next
junction takes its input from what is already there, here the `CreateUserRequest` the train started
with.

## Null assertion helpers

`Trax.Core.Extensions` has two extension methods that throw an `InvalidOperationException` naming
the expression that was null, in place of a `!` that would let the null through to fail later and
further away:

```csharp fragment
user.Profile.AssertLoaded();                     // "user.Profile has not been loaded"
orders.AssertEachLoaded(order => order.Customer); // names the collection and the selector
```

Both read the expression with `[CallerArgumentExpression]`, so the message says what was missing
without a debugger.

## Effects

Database writes, logging and the other side effects of a run are not part of these types. They are
the subject of [Effects](/docs/effect).

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Resolve](/docs/sdk-reference/train-methods/resolve) | [Run / RunEither](/docs/sdk-reference/train-methods/run)
