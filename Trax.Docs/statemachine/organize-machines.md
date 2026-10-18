---
title: Organize machines in a project
description: "Lay out a project's state machines one folder each: the machine, its states and triggers, its context, its event inputs, and the files generated from it."
type: how-to
area: machines
nav_order: 6
---

# Organize machines in a project

Give each machine a folder, as each train has one ([Organize trains in a
project](/docs/core/organize-trains)), beside the trains in the project that holds them. Its top
level holds the machine and the three types the machine is written in: its states, its triggers
and its context. Every other type it uses, each event's input and anything the context is made of,
goes in `Models/`:

```text
Machines/
└── Checkout/
    ├── CheckoutMachine.cs      the machine: Configure(), its rules
    ├── CheckoutState.cs        enum
    ├── CheckoutTrigger.cs      enum
    ├── CheckoutContext.cs      what an instance holds
    └── Models/
        ├── CheckoutStart.cs    the start's input
        ├── LinesInput.cs       an event's input
        └── CartLine.cs         part of the context
```

The machine's id (`checkout`) names its GraphQL field, its stored instances and the files generated
from it, so the folder is named after it.

## Put generated files under the solution, by machine id

`trax machine generate` writes the machine's exported definition and its differential corpus. Keep
them in one folder per machine, at the solution's root, where `trax machine check` and the tests
find them:

```text
machines/
└── checkout/
    ├── checkout.ir.json
    └── differential.json
```

Commit both, and embed the corpus in the project that holds the machine, so the machine can replay
it (`Corpus`). The frontend's generated twin goes beside the frontend, under the folder `trax client`
writes to.

## One file is fine for a small machine

`trax machine new` writes one file holding the enums, the context and the machine, and a machine
with two or three states reads well that way. Split it into the folder above once it has inputs of
its own.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [trax CLI](/docs/reference/cli) | [MachineConformance](/docs/sdk-reference/testing/effect-statemachine-testing)
