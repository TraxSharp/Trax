---
title: Trax in five minutes
description: "What Trax is and what it is for, in pictures: the problem, state machines and trains, how the server checks a move, AI agents, and who drives it."
type: explanation
area: general
section: none
nav_order: 0
---

# Trax in five minutes

This page explains what Trax is for, with no prior knowledge of it. Each section is one idea and
one picture, and they build on each other, so read it top to bottom. The example throughout is a
shop's checkout, the same one [the book](/docs/book) builds.

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

Trax is a .NET framework for enforced workflows. You describe the process and the work inside it in
C#, Trax serves it to whatever clients you have, and the server applies every rule itself, whoever
is asking.

## What a state machine is

![A traffic light is a state machine. It is always green, yellow or red. When the timer runs out it moves from green to yellow, yellow to red, and red back to green. Green straight to red is not a move.](/docs-images/traffic-light.svg)

The rest of this page builds on one idea. A state machine is anything that is always in exactly one
**state**, and changes only when an allowed **move** happens. A traffic light is green, yellow or
red, never two at once. When the timer runs out it moves to the next light, and some moves do not
exist at all: it cannot jump from green straight to red. States, moves, and **rules** about which
moves each state allows: that is the whole idea.

A checkout works the same way, with more states. Trax lets you write those rules once and has the
server enforce them.

## Think of it as a railway

![The railway picture. Stations Cart, Address, Payment, Placing and Placed sit on a track. A signal stands before Placing. The PlaceOrder train has left Placing and passes junctions on its way to Placed. Stations are states, tracks are moves, signals are guards, trains are the work, junctions are its steps.](/docs-images/railway.svg)

Trax already uses railway words, and the picture holds:

- **Stations are states**: where the process waits for someone to act.
- **Tracks are moves**: the only ways from one station to the next. There is no track from Cart
  straight to Placed.
- **Signals are guards**: checks a move must pass before it is allowed.
- **Trains are the work**, such as reserving stock or taking a payment. A train never waits: it
  leaves a station and runs without stopping to the next one. Not every move needs a train; here
  only Placing starts one.
- **Junctions are steps**: the points a train passes on its way. If you know railways, note that
  in Trax a junction is a step on a train's route, not a place where tracks split.

## A train does the work

![The PlaceOrder train. When every junction succeeds, PriceCart, ReserveStock, TakePayment and ConfirmOrder run in order and the caller gets the order. When ReserveStock fails because an item is out of stock, TakePayment and ConfirmOrder are skipped and the caller gets the exception.](/docs-images/place-order-train.svg)

A [train](/docs/core/trains-and-junctions) is one operation written as a short chain of small,
typed steps called junctions. When every junction succeeds, the caller gets the result. When one
fails, the rest are skipped and the caller gets the exception. Here no card is charged for an order
that cannot be filled. Every run of a train is recorded, so the failure can be found and read later.

## How states and trains work together

![Reaching the Placing station starts the PlaceOrder train. The train passes its junctions without stopping and returns a result: the checkout arrives at Placed when the train succeeds and at Failed when it does not.](/docs-images/machine-runs-train.svg)

Reaching the Placing station starts the `PlaceOrder` train. The train runs its junctions without
stopping and returns a result, and the result decides where the checkout arrives: Placed if it
succeeded, Failed if not.

A train is also useful on its own, for anything that runs from start to finish: from your own code,
as a GraphQL mutation, on a schedule, or on a worker in another process. Plenty of applications use
only trains. [Train or machine](/docs/statemachine/train-or-machine) helps you choose.

## The checkout as a map and a table

![The book's checkout drawn as a railway map. The main line runs Cart, Address, Payment, Placing, Placed, with the moves SetAddress, ChoosePayment and PlaceOrder between them and a guard before each. Placing runs the PlaceOrder train; if it fails the checkout goes to Failed, and Retry goes back to Placing. The customer is at Address.](/docs-images/checkout-map.svg)

The map is how you explain the checkout to anyone. The table is what a developer defines: every row
is a piece of track, and anything not in it is not a move, so the server refuses it.

| From | Move | Guard or rule | To |
|---|---|---|---|
| Cart | `SetAddress` | something is in the cart, and an address is given | Address |
| Address | `ChoosePayment` | a way to pay is given | Payment |
| Payment | `PlaceOrder` | the caller is a customer | Placing |
| Placing | the `PlaceOrder` train succeeds | | Placed |
| Placing | the `PlaceOrder` train fails | | Failed |
| Failed | `Retry` | the caller is a customer | Placing |

The customer is at Address, so the only way forward is `ChoosePayment`, and only with a way to pay.
The book's machine also has `Back` moves, a move that edits the cart, and a timer that abandons a
cart left alone for two hours; the picture leaves them out to stay small.

## What it looks like in C#

This is the same checkout in the book's code, with some lines left out:

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

[Author and serve a machine](/docs/statemachine/authoring) goes through every part.

## How the server checks a move

![A browser, a script and an AI agent each send a move. The server reads the stored state and checks three things: is the move allowed from here, may this caller make it, do the data and evidence hold up. All three pass and the new state is saved; any one fails and the client gets a typed error such as GUARD_FAILED, and nothing changes.](/docs-images/propose-decide.svg)

A client never tells the server "this order is now paid". It sends a move, such as `PlaceOrder`, and
the server works out the result from the state it stored, not from anything the client claims. It
checks three things:

1. Is this move allowed from the current state? If not, `NO_TRANSITION`.
2. May this caller make it? Nothing is served until a rule says who may, so if not, `FORBIDDEN`.
3. Do the data and evidence hold up? If not, `INVALID_INPUT` or `GUARD_FAILED`, with the reason.

If all three pass, the new state is saved. If any one fails, nothing changes and the client gets a
typed error saying which rule it broke. [Result codes](/docs/sdk-reference/statemachine-api/result-codes)
lists them all.

## Every client gets the same rules

![One workflow, defined once on the server, driven by four kinds of client. Web users get steps in order and valid data at each step. Schedules and queues get retries, dead letters and a record of every run. Scripts and services in any language get the same rules. AI agents get only the moves they are allowed, and evidence the server reruns.](/docs-images/many-drivers.svg)

Because the rules live on the server, every kind of client gets the same ones. People in a browser
get the steps in order and their input checked. Scheduled and queued work gets retries, dead letters
and a record of every run. Scripts and services call the same API in whatever language they are
written in, and the rules hold for them too, including the ones their language cannot check. AI
agents can only make the moves you declared, and the server reruns their evidence.

## Where Trax sits around an AI agent

![Two layers around an AI agent. The sandbox lets the agent run code, edit files, call tools and read data, but nothing in it reaches real systems. The only way out is through Trax's guards, which check that the move is allowed in this state, allowed for this agent, and that its evidence holds up. Only verified moves reach the real systems.](/docs-images/agent-sandbox.svg)

AI agents are the most demanding client. Two layers do two different jobs. A sandbox limits what an
agent can do: it can run code and make a mess, but only in its own box. Trax limits what it can
change: its only way out is a small set of declared moves, each checked against the state, the
agent's role, and evidence the server checks itself rather than takes on the agent's word. Most
agent setups have the first layer and improvise the second with instructions in a prompt.

## The Board sample

![The Board sample. A bug ticket moves through five lanes, each owned by one client: Triage by triage-bot, Reproduce by repro-bot, Fix by fix-bot, QA by qa-bot and Review by Sam, a person. An agent submits its evidence, a check train on the server reruns it, and the ticket moves only if the check accepts it.](/docs-images/board-lanes.svg)

[Board](/docs/samples/board) is a Trax sample where bots move bug tickets through lanes. Four
scripted bots and one person each own a lane, with keys of their own. A bot never moves a ticket
itself: it submits evidence for its lane, a check train on the server reruns that evidence against
the rules, and only the train's verdict moves the ticket. The last lane belongs to a person, so
human sign-off is just another lane with a role. The bots are scripted, not language models, but
they talk to the server the way an AI agent would.

What an agent cannot do there, and what it gets back:

| The agent tries to | The server answers | Because |
|---|---|---|
| Claim a ticket another agent holds | `ALREADY_CLAIMED` | a claim needs a ticket nobody holds |
| Approve its own fix | `FORBIDDEN` | only a reviewer may approve |
| Close a ticket as not reproducible too early | `GUARD_FAILED` | closing needs at least two cases the server ran |
| Claim work when none is waiting | `NOTHING_TO_CLAIM` | there is no ticket in its lane |

An agent cannot report its own test results either: the cases on record are written only by the
server's check.

## Every run is recorded

![A run's timeline: it starts, PriceCart and ReserveStock finish, a checkpoint is saved, the process crashes, and the run resumes from the checkpoint instead of starting over, then finishes.](/docs-images/run-record.svg)

Every run is a row in your own Postgres (or SQLite) database: when it started, how it ended, its
input and output, and which junction failed with what. The [operations console](/docs/dashboard)
lists them, so an operator can find a run, read it and requeue it. A train can save a
[checkpoint](/docs/core/checkpoints-and-branches), so a failed run resumes from there instead of
from the start, and answers it already recorded are replayed rather than asked again, which matters
when a model gave them. The [Recovery](/docs/samples/recovery) sample shows it live.

## Explaining Trax to someone else

The idea is the same for everyone; only the words change. Start at the top and go further only if
they ask.

| To | Say |
|---|---|
| Anyone | Trax makes sure a process happens in the right order, by the right people, with nothing skipped, whether a person, a script or an AI is doing the work. |
| Engineers | You define a workflow once as a state machine. Any client can drive it, but the server checks every move and rejects bad ones with clear errors. |
| Agent builders | Each agent works through its own state machine. It can only send the moves you declared, and the server reruns its evidence before anything advances. |
| .NET developers | State machines and typed trains in C#, transitions the server recomputes, and every run recorded in Postgres. Add the scheduler, GraphQL and workers when you need them. |

## Where to go next

| You want to | Read |
|---|---|
| Try it now | [Quick start](/docs/getting-started), one app from an empty folder |
| Learn it properly | [The Trax book](/docs/book), a shop built one feature per chapter |
| See it working | [Samples](/docs/samples), complete applications you can run |
| Compare it with Hangfire and Quartz.NET | [Trax compared with Quartz.NET and Hangfire](/docs/reference/comparison) |
| Look up a word | [Glossary](/docs/reference/glossary) |
| See everything it does | [What Trax does](/docs/what-trax-does), every capability and where it is taught |

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [Result codes](/docs/sdk-reference/statemachine-api/result-codes) | [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train)
