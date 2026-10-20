---
title: Train or machine
description: "When to write a train and when to write a state machine: one operation that runs to its end, or a long-lived process a user moves through, and how they combine."
type: explanation
area: machines
nav_order: 1
---

# Train or machine

Trax has two ways to describe work, and they answer different questions.

A **train** is one operation that runs from start to end: place an order, sync a supplier, send a
report. Someone (a caller, the scheduler, a worker) starts it, its junctions run in order, and a
few milliseconds or minutes later it has finished, with a result or an exception and a run record
saying which. Nothing about it waits for a person.

A **machine** is a thing that exists for a while and changes when someone acts on it: a checkout,
an application form, an onboarding. It sits in a named state (`Cart`, then `Placing`, then `Placed`) for as
long as it takes, hours or weeks. Each action a user takes is an event the server applies or
refuses, and the state and the data that goes with it are stored after every move, so the user can
leave and come back on another device. The browser runs the same machine in TypeScript, so it knows
before asking which moves are allowed.

| | Train | Machine |
|---|---|---|
| Lives for | one run | as long as the process it models |
| Moves when | its previous junction finishes | someone sends it an event, or a timer fires |
| Stored as | a run record, after it ends | a snapshot, after every move |
| Fails by | throwing, which stops the rest of the chain | refusing an event, which leaves it where it was |
| Started by | your code, GraphQL, the scheduler, a worker, a machine | a client over GraphQL, or a parent machine |

## Use a train when

- the work has a clear start and end, and nobody waits in the middle;
- it should be scheduled, retried, queued or moved to a worker;
- the question is "did it work, and if not, which step failed".

## Use a machine when

- a person, or another system, takes several separate actions over time;
- some actions are only allowed in some states, and the browser should know which;
- the question is "where is this one right now, and what can happen next".

## Both

Most applications need both, and they meet in one place: a state can [invoke a
train](/docs/statemachine/invoking-trains). The machine decides *when* the work happens, the train
does it, and the train's result moves the machine on. In the book's shop, checkout is a machine,
and when it reaches `Placing` it runs the `PlaceOrder` train and moves to `Placed` or to `Failed`
with the outcome ([chapter 13](/docs/book/the-machine-places-the-order)).

A long train is not a reason to use a machine. A train that does a lot of work can save
[checkpoints](/docs/core/checkpoints-and-branches) so a failed run resumes after the last one. A
machine is for work that waits on someone.
