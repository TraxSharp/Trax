---
title: Write machines as features
description: "Write machines as Gherkin features and a glossary, derive them with trax machine from-gherkin, serve them, and run the features in memory and against the host."
type: how-to
area: machines
nav_order: 24
---

# Write machines as features

A team that describes what happens in Gherkin can have Trax derive the machines from it: the states, the moves, the
guards, the timers, the policies between machines, the views, and who may do what. Nothing is written in C# but the
trains the machines invoke. Why this is a source of machines, and what it refuses, is
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md).
The [Bakery sample](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/Bakery/README.md) is this page,
built, and [How features become machines](/docs/statemachine/how-features-become-machines) follows each glossary
entry and step to the IR, `host.json`, GraphQL and the C# builder.

## Write the glossary

The glossary names the words the features use: the actors and the roles they carry, each machine with its states,
triggers and guards, and the views. A user's machine is owned by an actor; a system machine lives at a fixed key.

```json
{
  "version": 1,
  "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
  "machines": {
    "order": {
      "ownedBy": "customer",
      "noun": "order",
      "start": "has an order",
      "initial": "Draft",
      "states": { "Draft": "is a draft", "Placed": "is placed" },
      "triggers": { "Place": { "phrase": "places {subject}" } }
    }
  }
}
```

Every key is in the [feature files reference](/docs/reference/feature-files#the-glossary).

## Write the features

Each scenario names its people in the third person, sets up what it needs with Givens, acts with Whens, and says what
happened with Thens:

```gherkin
Feature: Ordering bread
  Scenario: A customer places an order
    Given Ada is a customer
    And Ada has an order
    When Ada places her order
    Then Ada's order is placed
```

An actor's own act grants that actor the trigger. A trigger no scenario shows anyone firing is not served at all. The
steps derivation reads are in the [reference](/docs/reference/feature-files#the-steps); a Then it does not read is
about the UI, and is left alone.

## Add guards, trains, timers, policies and views

Each of these is a glossary entry plus the scenarios that show it. The examples are the Bakery's.

**A guard** is a pair of phrases and the rule they stand for. A scenario where it holds and one where it fails, with
the message, make a guarded move:

```json
"guards": {
  "hasLoaves": {
    "holds": "has something in it",
    "fails": "has nothing in it",
    "rule": { "rule": "compare", "source": "context", "field": "loaves", "op": "gt", "value": 0 }
  }
}
```

```gherkin
Scenario: An empty order is refused
  Given Ada's order has nothing in it
  When Ada places her order
  Then Ada is told "Add something first."
  And Ada's order is still a draft
```

**A train** runs when an instance enters a state. The glossary names its interface, its input from context fields,
its output, and a phrase for each outcome, with the output an in-memory run answers with:

```json
"invokes": {
  "Paying": {
    "train": "Trax.Samples.Bakery.Trains.ITakePaymentTrain",
    "input": { "loaves": "loaves" },
    "output": [ { "name": "approved", "type": "boolean", "nullable": false, "constraints": [] } ],
    "outcomes": {
      "the payment goes through": { "outcome": "done", "when": { "rule": "boolEquals", "source": "input", "field": "approved", "value": true } },
      "the payment is declined": { "outcome": "done", "when": { "rule": "boolEquals", "source": "input", "field": "approved", "value": false } },
      "the payment cannot be taken": { "outcome": "failed" },
      "the payment is called off": { "outcome": "cancelled" }
    },
    "examples": {
      "the payment goes through": { "approved": true },
      "the payment is declined": { "approved": false }
    }
  }
}
```

```gherkin
When Ada places her order
Then Ada's order is being paid for
When the payment goes through
Then Ada's order is placed
```

Every invoking state needs a scenario for each of `done`, `failed` and `cancelled`. The train is the one piece of
C# behind the machine, written as in [Invoke a train from a state](/docs/statemachine/invoking-trains).

**A timer** is time passing in a scenario. There is no glossary entry:

```gherkin
Scenario: An order comes out of the oven
  Given Ada's order is baking
  When 10 seconds pass
  Then Ada's order is ready to collect
```

**A policy** is a Then about another instance moving after a When. A system machine has a fixed key and a name, and
only the server moves it:

```json
"shop": {
  "system": "shop/main",
  "name": "the shop",
  "initial": "Closed",
  "states": { "Closed": "is closed", "Open": "is open" },
  "triggers": { "Open": { "phrase": "the shop opens" } }
}
```

```gherkin
Scenario: A baker clocking in opens the shop
  Given Kit is a baker
  And Kit has a shift
  And the shop is closed
  When Kit clocks in for his shift
  Then Kit's shift is on
  And the shop is open
```

That derives a `toSystem` policy: when a shift enters `On`, the server fires `Open` on `shop/main`. `Open` also needs
a scenario of its own (`Given the shop is closed` / `When the shop opens` / `Then the shop is open`). Two machines of
the same owner need a [link](/docs/reference/feature-files#links), and a system machine moving every waiting user
instance is a `broadcast`.

**A view** is what a reader sees across machines, with labels for states. A scenario that reads it makes its actor a
reader, and a view no scenario reads is not served:

```json
"views": {
  "orders": {
    "reads": [ { "machine": "order" } ],
    "fields": {
      "status": { "from": "order.state", "labels": { "Placed": "Waiting for the oven", "Baking": "In the oven" } },
      "loaves": { "from": "order.loaves" }
    }
  }
}
```

```gherkin
Then Ada's orders show status "In the oven"
```

## Derive the machines

```bash
trax machine from-gherkin --features features --out machines
```

It writes one `<machine>.ir.json` per machine, `host.json` with what only the server reads, and `access.txt`, one line
per grant. It replays every scenario on what it derived, and refuses features that contradict each other, or that
would derive a machine the host refuses, naming the file and line. Commit `machines/`, and review `access.txt` in every
pull request: a new grant shows there. `trax machine from-gherkin --check` in CI fails when a committed file is stale.

`trax machine missing --features features` lists what the glossary names and no scenario shows, each with a scenario
to paste that is checked green first. `trax machine explain --features features` prints each scenario with what
derivation read from it, a policy in full among them: run it when a scenario does not derive what you meant, and read
[how a scenario is read](/docs/reference/feature-files#how-a-scenario-is-read).

## Serve them

```csharp fragment
services.AddTrax(trax => trax
    .AddEffects(e => e.UsePostgres(connectionString).AddJunctionProgress())
    .AddStateMachines(o => o.ServeFeatures("machines", typeof(TakePaymentTrain).Assembly))
    .AddMediator(typeof(TakePaymentTrain).Assembly)
    .AddScheduler());
```

[`ServeFeatures`](/docs/sdk-reference/statemachine-api/add-state-machines) serves the directory as committed: each
machine with its access, the policies between them, the views and the system instances. A directory whose files
disagree refuses the host at startup. Copy `machines/` into the build output, and export the client manifest and run
`trax client` as for any machine.

A state that invokes a train only queues its run. `AddScheduler()`, on this host or another sharing the database, runs
it, and without one the instance waits in that state. See
[Meet the host's requirements](/docs/statemachine/invoking-trains#3-meet-the-hosts-requirements).

## Run the features

- **In memory**, on the committed machines: `trax machine run --features features --machines machines`, or
  [`FeatureMachines.Run`](/docs/sdk-reference/statemachine-api/feature-machines) from a test. Each answer is the
  host's, access included.
- **Against the host**, with [Reqnroll](/docs/sdk-reference/statemachine-api/feature-bindings): reference
  `Trax.StateMachine.Gherkin.Reqnroll`, include the feature files, and configure the bindings with the host in the
  test process and a credential per person per scenario. A machine feature needs no step code. What the machines do not
  cover goes in specs under `specs/`, which derivation does not read, with the few steps the glossary does not read
  registered beside the bindings.

Nothing runs a feature through a page yet. A page's browser tests are written by hand, as the Bakery's
`BakeryPageTests` are: two people on pages of their own, telling the same story as the features. Nothing checks that
they still do when a scenario changes.

## SDK Reference

> [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines)
