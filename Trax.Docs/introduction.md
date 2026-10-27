---
title: Trax in five minutes
description: "What Trax is and what it is for, in pictures and without code: the problem, state machines and trains, how the server decides, and who drives it."
type: explanation
area: general
section: none
nav_order: 0
---

# Trax in five minutes

This page explains what Trax is for, with no code and no prior knowledge of it. Each section is one
idea and one picture, and they build on each other, so read it top to bottom. The example throughout
is a shop's checkout, the same one [the book](/docs/book) builds.

## Every process has rules

A checkout goes from cart to address to payment to a placed order, in that order. A refund needs a
manager. An invoice has to add up. Every process has rules like these, and more and more different
clients touch the same process: people in a browser, scripts and other services, scheduled jobs,
and now AI agents. Any of them can ask for something it should not: skip straight to payment, send
data of the wrong shape, approve its own work.

In most systems the rules that stop them are checks spread through the code, and for an AI agent,
instructions in a prompt it may not follow.

## The idea

Define the process once, on the server. Let anything drive it. Check every move.

Trax is a .NET framework for that. You describe the process and the work inside it in C#, Trax
serves it to whatever clients you have, and the server applies every rule itself, whoever is
asking.

## Think of it as a railway

![A railway with four stations, Cart, Address, Payment and Placed. A train runs between the first two, and a signal stands before the last. Stations are states, tracks are allowed moves, signals are server checks, trains are the work.](/docs-images/railway.svg)

Trax already uses railway words, and the picture holds. The stations are the states the process can
be in. The tracks are the only moves between them: there is no track from Cart straight to Placed.
The signals are the server's checks, which every move has to pass. The trains are the actual work,
such as reserving stock or taking a payment, that runs between stations.

## A state machine maps the process

![The checkout machine. Cart, Address, Payment and Placing in a row, joined by the moves SetAddress, ChoosePayment and PlaceOrder; Placing ends in Placed or Failed. The checkout is at Address. A PlaceOrder straight from Cart is crossed out: the server refuses it with NO_TRANSITION.](/docs-images/checkout-machine.svg)

A [state machine](/docs/statemachine) is a map of a process: its states, and the moves allowed from
each one. A move is an event a client sends, such as `ChoosePayment`, and it can carry input that
the machine checks before it moves. A move that is not on the map does not happen, and the client is
told why.

The machine is written once, in C#. The browser gets the same machine in TypeScript, so it knows
which buttons to show before it asks, but the server still decides.

## A train does the work

![The PlaceOrder train. When every junction succeeds, PriceCart, ReserveStock, TakePayment and ConfirmOrder run in order and the caller gets the order. When ReserveStock fails because an item is out of stock, TakePayment and ConfirmOrder are skipped and the caller gets the exception.](/docs-images/place-order-train.svg)

The work itself is a [train](/docs/core/trains-and-junctions): one operation written as a short
chain of small steps called junctions. When every junction succeeds, the caller gets the result.
When one fails, the rest are skipped and the caller gets the exception. Here no card is charged for
an order that cannot be filled. Every run of a train is recorded, so the failure can be found and
read later.

## How they fit

![When the checkout enters Placing, the machine runs the PlaceOrder train. The checkout moves to Placed if the train succeeds and to Failed if it does not.](/docs-images/machine-runs-train.svg)

The machine decides when work happens, and the train does it. When the checkout reaches `Placing`,
the machine runs `PlaceOrder`, and the train's result decides the next state.

A train is also useful on its own, for anything that runs from start to finish: from your own code,
as a GraphQL mutation, on a schedule, or on a worker in another process. Plenty of applications use
only trains. [Train or machine](/docs/statemachine/train-or-machine) helps you choose.

## Clients propose, the server decides

![A browser, a script and an AI agent each propose a move. The server reads the stored state and asks three questions: is the move allowed from here, may this caller make it, does the input hold up. All three pass and the new state is saved; any one fails and the client gets a typed error such as GUARD_FAILED, and nothing changes.](/docs-images/propose-decide.svg)

A client never tells the server "this order is now paid". It sends a move, such as `PlaceOrder`, and
the server works out the result from the state it stored, not from anything the client claims. It
asks three questions:

1. Is this move allowed from the current state? If not, `NO_TRANSITION`.
2. May this caller make it? Nothing is served until a rule says who may, so if not, `FORBIDDEN`.
3. Does the input hold up? If not, `INVALID_INPUT` or `GUARD_FAILED`, with the reason.

If all three pass, the new state is saved. If any one fails, nothing changes and the client gets a
typed error saying which rule it broke. [Result codes](/docs/sdk-reference/statemachine-api/result-codes)
lists them all.

## One process, many drivers

![One process defined once on the server, driven by four kinds of client: browsers, schedules and queues, scripts and services, and AI agents.](/docs-images/many-drivers.svg)

Because the rules live on the server, every kind of client gets the same ones. People in a browser
get the steps in order and their input checked. Scheduled and queued work gets retries, dead letters
and a record of every run. Scripts and services call the same API in whatever language they are
written in, and the rules hold for them too.

AI agents are the most demanding client. A sandbox limits what an agent can do while it works; Trax
limits what it can change. Its only way to affect the process is a small set of declared moves, each
checked against the state, the agent's role, and evidence the server checks itself rather than takes
on the agent's word. The [Board](/docs/samples/board) sample shows this with scripted agents working
bug tickets: none of them can claim a ticket someone else holds, approve its own fix, or report a test
the server never ran.

## Every run is recorded

![A run's timeline: started, PriceCart done, ReserveStock done, a checkpoint saved, a crash, then the run resumes from the checkpoint and finishes.](/docs-images/run-record.svg)

Every run is a row in your own Postgres (or SQLite) database: when it started, how it ended, its
input and output, and which junction failed with what. The [operations console](/docs/dashboard)
lists them, so an operator can find a run, read it and requeue it. A train can save a
[checkpoint](/docs/core/checkpoints-and-branches), so a failed run resumes from there instead of
from the start, and answers it already recorded are replayed rather than asked again, which matters
when a model gave them. The [Recovery](/docs/samples/recovery) sample shows it live.

## Where it fits

If you know Hangfire or Quartz.NET: Hangfire is the simple choice for background jobs, and Quartz
for precise time-based triggers. Trax is for work with several steps that has to follow rules and
leave a record. They can work together, since a Hangfire or Quartz job can call a Trax train.
[Trax compared with Quartz.NET and Hangfire](/docs/reference/comparison) goes through it in detail.

Trax is MIT licensed, targets .NET 10, and stores its data in PostgreSQL or SQLite.

## Where to go next

| You want to | Read |
|---|---|
| Try it now | [Quick start](/docs/getting-started), one app from an empty folder |
| Learn it properly | [The Trax book](/docs/book), a shop built one feature per chapter |
| See it working | [Samples](/docs/samples), complete applications you can run |
| Look up a word | [Glossary](/docs/reference/glossary) |
| See everything it does | [What Trax does](/docs/what-trax-does), every capability and where it is taught |
