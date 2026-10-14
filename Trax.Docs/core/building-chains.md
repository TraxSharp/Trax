---
title: How a chain runs
description: "What each step of a train's chain (Chain, ShortCircuit, Extract, AddServices, Resolve) does to Memory, and how it behaves once the train is on the left track."
type: explanation
area: trains
nav_order: 5
source:
  - Trax.Core/src/Trax.Core/Monad
---

# How a chain runs

A train's route is the chain it declares in `Junctions()`. Each step reads from
[Memory](/docs/core/memory), may add to it, and either keeps the train on the right track or
switches it to the left. Once on the left track, every later step is skipped and the exception is
what the train returns. This page describes how each kind of step behaves; the overloads and
exact rules are on the [Train Methods](/docs/sdk-reference/train-methods) pages.

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Train;

public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>() // throws: the train switches to the left track
            .Chain<CreateUserJunction>() // skipped after a failure
            .Chain<SendEmailJunction>() // skipped after a failure
            .Resolve(); // the exception, or the User in Memory
}
```

The chain is declared once and read at startup as well as at run time, so it cannot branch on its
input: the host's
[startup check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)
replays it against Memory and refuses a chain whose junction inputs or return type never arrive.
A route that does differ per run declares every track and lets a [decider](/docs/core/decisions)
pick one.

## Chain

[`Chain<TJunction>()`](/docs/sdk-reference/train-methods/chain) is the ordinary step. It builds the
junction, takes its input from Memory, runs it, and stores the output in Memory under the output's
type. A junction that throws switches the train to the left track, and the exception passes every
later step on its way to the caller.

## ShortCircuit

[`ShortCircuit<TJunction>()`](/docs/sdk-reference/train-methods/short-circuit) runs a junction
whose output, when it has one, is the train's answer. If the junction returns a value of the
train's return type, that value is captured and `Resolve()` returns it instead of looking in
Memory. If the junction throws, the exception is swallowed and the train stays on the right track:
for a short circuit, a failure means "no early answer, keep going". That is the inverse of
`Chain`, on purpose.

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Train;

public class ProcessOrderTrain : Train<OrderRequest, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrderJunction>()
            .ShortCircuit<CheckCacheJunction>() // a cached result, if there is one
            .Chain<CalculatePricingJunction>() // still runs
            .Chain<ProcessPaymentJunction>() // still runs
            .Chain<SaveOrderJunction>()
            .Resolve();
}
```

The steps after a successful short circuit still run. The captured value changes only what
`Resolve()` returns, and a later junction that fails puts the train on the left track, where
`Resolve()` returns that failure rather than the captured value. Nothing in a chain skips the
remaining junctions and still returns the short-circuit value, because a chain cannot branch on
what happened at run time (`Trax.Docs/adr/0016`). A junction after a `ShortCircuit` that should
not repeat work decides that itself, from what it is given.

It suits caching, feature flags that return a default, and preconditions that are already met.

## Extract

[`Extract<TSource, TTarget>()`](/docs/sdk-reference/train-methods/extract) finds the `TSource` in
Memory, reads its first public instance property, then field, of type `TTarget`, and stores that
value in Memory under `TTarget`. It saves writing a junction whose only job is to read a property:

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Train;

public class VerifyUserEmailTrain : Train<UserId, Unit>
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LoadUserJunction>() // User in Memory
            .Extract<User, EmailAddress>() // User.Email in Memory as EmailAddress
            .Chain<VerifyEmailAddressJunction>()
            .Resolve();
}
```

A missing `TSource`, or no non-null member of type `TTarget`, switches the train to the left
track. Anything beyond a property read (a null check, a transformation, validation) belongs in a
junction.

## AddServices

[`AddServices(...)`](/docs/sdk-reference/train-methods/add-services) stores the instances it is
given in Memory, each under its type argument, bypassing the container. Later junctions take them
as input, or as constructor arguments, as they would anything else in Memory:

```csharp compile
using Trax.Core.Functional;
using Trax.Core.Train;

public class RegisterUserTrain(IValidator validator, INotifier notifier)
    : Train<CreateUserRequest, User>
{
    protected override Task<Either<Exception, User>> Junctions() =>
        AddServices(validator, notifier)
            .Chain<ValidateJunction>() // takes IValidator from Memory
            .Chain<CreateUserJunction>()
            .Chain<NotifyJunction>() // takes INotifier from Memory
            .Resolve();
}
```

It is for instances made per run or kept outside the container; ordinary dependencies are
constructor injection on the junction. The type arguments must be interfaces, and the instances
must already exist when `Junctions()` runs, since the chain is also read at startup. Setup that
needs async work, a try/catch, or a nested train's result belongs in a junction at the head of the
chain, whose output lands in Memory for the junctions after it.

## Resolve

[`Resolve()`](/docs/sdk-reference/train-methods/resolve) ends the chain. It returns, in order: the
exception if the train is on the left track, the short-circuit value if one was captured, or the
train's return type from Memory. A return type that never reached Memory is a failure, and the
startup check refuses such a chain before the host serves traffic.

On a train whose return type is already in Memory because it is the input type or `Unit`, the
chain names no junctions and the whole declaration is `Task.FromResult(Resolve())`: the train's
own `Resolve()` is synchronous, so it is wrapped to match the `Task` that `Junctions()` returns.

A `Resolve(value)` overload exists on the chain, but a declared chain that ends with it is refused
when the chain is read: it states the result instead of declaring the junction that produces it.
To merge a nested train's result into the output, the junction that calls the nested train returns
the merged value.

## Other steps

[`Decide`, `Switch`, `Gate` and `Scale`](/docs/core/decisions) route a run down one of several
declared tracks. [`Parallel`](/docs/sdk-reference/train-methods/parallel) runs a fixed set of
named branches side by side, each on its own copy of Memory, then joins them.
[`Checkpoint`](/docs/sdk-reference/train-methods/checkpoint) stores a state so that a later run of
the same input resumes after it.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) | [Extract](/docs/sdk-reference/train-methods/extract) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [Resolve](/docs/sdk-reference/train-methods/resolve)
