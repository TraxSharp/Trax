---
title: Trax in five minutes
description: "What Trax is and what it is for, in pictures: the problem, state machines and trains, and how the server checks a move."
type: explanation
area: general
section: none
nav_order: 0
---

# Trax in five minutes

The pictures use one colour per idea, the same everywhere: teal for states, lavender for moves,
amber for guards, blue for trains, pink for junctions, and orange for failures.

## Many kinds of clients touch the same process

![A web user, a script and an AI agent all reach the same process. The web user skips ahead to payment, the script sends data in the wrong shape, the agent approves its own work. The process's rules usually live in checks spread through the code, or in a prompt an AI may ignore.](/docs-images/problem.svg)

A checkout goes from cart to address to payment to a placed order, in that order. A refund needs a
manager. An invoice has to add up. Every process has rules like these, and more and more different
clients touch the same process: people in a browser, scripts and other services, scheduled jobs,
and now AI agents that call tools on their own. Any of them can try something it should not.

In most systems the rules that stop them are checks spread through the code, and for an AI agent,
instructions in a prompt it may not follow.

## The idea

Define the process once, on the server. Let anything drive it. Check every move.

Trax is a .NET framework for enforced workflows. It is a set of NuGet packages you add to your own
ASP.NET Core app, so "the server" on this page is your server, with Trax inside it. You describe the
process and the work inside it in C#, Trax serves it to whatever clients you have, and Trax applies
every rule itself, whoever is asking.

The split, which the rest of this page comes back to:

| You write | Trax does |
|---|---|
| The states, and the moves allowed from each | Stores the state, and refuses any move you did not list |
| Who may send each move | Reads the caller from your app's sign-in, and refuses anyone your rule leaves out |
| The guards, and the reason each one gives back | Runs them against the stored state, and returns your reason when one fails |
| The trains: the work, as small steps called junctions | Runs them in order, stops at the first failure, and records every run |
| A few lines in `Program.cs` that register Trax | Serves your machines and trains over GraphQL, and generates a typed TypeScript client |
| When background work runs, and how often to retry it | Queues it, retries it, dead-letters it, and shows it in the operations console |

The pictures mark the code you write with an amber dashed tag.

## What a state machine is

![A traffic light is a state machine. It is always green, yellow or red. When the timer runs out it moves from green to yellow, yellow to red, and red back to green. Green straight to red is not a move.](/docs-images/traffic-light.svg)

The rest of this page builds on one idea. A state machine is anything that is always in exactly one
**state**, and changes only when an allowed **move** happens. A traffic light is green, yellow or
red, never two at once. When the timer runs out it moves to the next light, and some moves do not
exist at all: it cannot jump from green straight to red. States, moves, and **rules** about which
moves each state allows: that is the whole idea.

A checkout works the same way, with more states. Trax lets you write those rules once and has the
server enforce them.

The pictures below draw a process as a railway, because Trax already uses railway words: stations are states, tracks
are moves, signals are guards, trains are the work, and junctions are a train's steps. A junction here is a step on a
train's route, not a place where tracks split. The [Glossary](/docs/reference/glossary#the-railway-words) has the
whole picture.

## A train does the work

![The PlaceOrder train runs four junctions in order: PriceCart, ReserveStock, TakePayment and ConfirmOrder. When all succeed the caller gets the order. When ReserveStock fails because an item is out of stock, TakePayment and ConfirmOrder are skipped, no card is charged, and the caller gets the exception. Each junction is code you write; Trax runs them in order, skips the rest when one fails, and records the run.](/docs-images/place-order-train.svg)

A [train](/docs/core/trains-and-junctions) is one operation written as a short chain of small,
typed steps called junctions. Each junction is a class you write, such as the one that charges the
card. The chaining is Trax's: when every junction succeeds, the caller gets the result, and when one
fails, Trax skips the rest and the caller gets the exception. Here no card is charged for an order
that cannot be filled. Trax records every run of a train, so the failure can be found and read
later.

## How states and trains work together

![Reaching the Placing station starts the PlaceOrder train. The train passes its junctions without stopping and returns a result: the checkout arrives at Placed when the train succeeds and at Failed when it does not.](/docs-images/machine-runs-train.svg)

You declare that Placing runs the `PlaceOrder` train, and where each outcome leads. Trax does the
rest: when the checkout reaches Placing it starts the train, the train runs its junctions without
stopping, and Trax moves the checkout on from the result: to Placed if it succeeded, to Failed if
not. No code of yours polls for the result or moves the checkout by hand.

A train is also useful on its own, for anything that runs from start to finish: from your own code,
as a GraphQL mutation, on a schedule, or on a worker in another process. Plenty of applications use
only trains. [Train or machine](/docs/statemachine/train-or-machine) helps you choose.

## What it looks like in C#

![The book's checkout drawn as a railway map. The main line runs Cart, Address, Payment, Placing, Placed, with the moves SetAddress, ChoosePayment and PlaceOrder between them and a guard before each. Placing runs the PlaceOrder train; if it fails the checkout goes to Failed, and Retry goes back to Placing. The customer is at Address.](/docs-images/checkout-map.svg)

The book's checkout runs Cart, Address, Payment, Placing, Placed, and back to Placing from Failed. Anything not
declared is not a move, so the server refuses it. This is the same checkout in the book's code, with some lines left
out:

```csharp from=Trax.Samples/book/Shopfront/Shopfront.Core/Machines/Checkout/CheckoutMachine.cs
m.In(CheckoutState.Address)
// ...
    .On(CheckoutTrigger.ChoosePayment)
    .AnyAuthenticated()
    .WithInput<PaymentInput>()
    .When(Input((PaymentInput i) => i.PaymentToken).NonEmpty())
    .Because("Choose how to pay.")
// ...
    .To(CheckoutState.Payment);

m.In(CheckoutState.Payment)
// ...
    .On(CheckoutTrigger.PlaceOrder)
    .Requires(Roles.Customer)
    .To(CheckoutState.Placing);
// ...
m.In(CheckoutState.Placing)
    .Context<CheckoutContext>()
    .Invokes<IPlaceOrderTrain, PlaceOrderInput, PlaceOrderOutput>(OrderFor)
    .OnDone(
        CheckoutState.Placed,
// ...
    .OnFailed(CheckoutState.Failed)
    .OnCancelled(CheckoutState.Failed);

m.In(CheckoutState.Failed)
    .Context<CheckoutContext>()
    .On(CheckoutTrigger.Retry)
    .Requires(Roles.Customer)
    .To(CheckoutState.Placing);
```

- `In` and `To` name the state a move starts from and the state it leads to.
- `On` names a move a client may send in that state.
- `AnyAuthenticated` and `Requires` say who may send it.
- `When` is the guard, and `Because` is the reason the client gets back when it fails, written
  next to the rule.
- `Invokes` names the train a state runs, and `OnDone` and `OnFailed` where its result leads.

That is all you write for the checkout's rules. From it, Trax serves each move as a GraphQL
mutation, generates a TypeScript client that knows which moves each state allows, stores each
checkout's state in your database, and refuses any move the block does not list. There is no
controller to write, no table for the checkout's state, and no `if` that checks whether the cart was
paid.

[Author and serve a machine](/docs/statemachine/authoring) goes through every part.

## How the server checks a move

![Outside Trax, a browser, a script and an AI agent each send a move, never the new state; the client code is yours or generated by Trax. Everything inside the dashed line is Trax. The move arrives through Trax's API, which knows who is calling. Trax then checks it against the stored state: is the move allowed from here, which Trax decides alone; may this caller make it, by rules you write; and do the data and evidence hold up, by guards you write. All three pass and Trax saves the new state and runs the work you wrote. Any one fails and the client gets a code such as GUARD_FAILED or FORBIDDEN with your reason, and nothing changes.](/docs-images/propose-decide.svg)

A client never tells the server "this order is now paid". It sends a move, such as `PlaceOrder`, and
Trax works out the result from the state it stored, not from anything the client claims. It checks
three things, each against something you wrote in the C# above:

1. Is this move allowed from the current state? Your `In` and `On` lines say which moves each state
   allows. If not, `NO_TRANSITION`.
2. May this caller make it? Your `Requires` or `AnyAuthenticated` says who may. A move with no such
   rule is refused to everyone, so forgetting one locks the move rather than opening it. If not,
   `FORBIDDEN`.
3. Do the data and evidence hold up? Your `When` guards decide, and their `Because` is the reason
   sent back. If not, `INVALID_INPUT` or `GUARD_FAILED`.

If all three pass, Trax saves the new state and starts any train the new state runs. If any one
fails, nothing changes and the client gets a typed error saying which rule it broke. [Result codes](/docs/sdk-reference/statemachine-api/result-codes)
lists them all.

## Where to go next

| You want to | Read |
|---|---|
| Try it now | [Quick start](/docs/getting-started), one app from an empty folder |
| Learn it properly | [The Trax book](/docs/book), a shop built one feature per chapter |
| See it working | [Samples](/docs/samples), complete applications you can run |
| Put AI agents to work on it | [Trax and AI agents](/docs/ai-agents), and the [Board](/docs/samples/board) sample |
| Compare it with what you use now | [Trax compared](/docs/reference/comparison) with Temporal, Durable Functions, Elsa, MassTransit, XState, Hangfire and Quartz.NET |
| Look up a word | [Glossary](/docs/reference/glossary) |
| See everything it does | [What Trax does](/docs/what-trax-does), every capability and where it is taught |

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [Result codes](/docs/sdk-reference/statemachine-api/result-codes) | [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train)
