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
built.

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

## Derive the machines

```bash
trax machine from-gherkin --features features --out machines
```

It writes one `<machine>.ir.json` per machine, `host.json` with what only the server reads, and `access.txt`, one line
per grant. It replays every scenario on what it derived, and refuses features that contradict each other, or that
would derive a machine the host refuses, naming the file and line. Commit `machines/`, and review `access.txt` in every
pull request: a new grant shows there. `trax machine from-gherkin --check` in CI fails when a committed file is stale.

`trax machine missing --features features` lists what the glossary names and no scenario shows, each with a scenario
to paste that is checked green first.

## Serve them

```csharp fragment
services.AddTrax(trax => trax
    .AddEffects(e => e.UsePostgres(connectionString).AddJunctionProgress())
    .AddStateMachines(o => o.ServeFeatures("machines", typeof(TakePaymentTrain).Assembly))
    .AddMediator(typeof(TakePaymentTrain).Assembly));
```

[`ServeFeatures`](/docs/sdk-reference/statemachine-api/add-state-machines) serves the directory as committed: each
machine with its access, the policies between them, the views and the system instances. A directory whose files
disagree refuses the host at startup. Copy `machines/` into the build output, and export the client manifest and run
`trax client` as for any machine.

## Run the features

- **In memory**, on the committed machines: `trax machine run --features features --machines machines`, or
  [`FeatureMachines.Run`](/docs/sdk-reference/statemachine-api/feature-machines) from a test. Each answer is the
  host's, access included.
- **Against the host**, with [Reqnroll](/docs/sdk-reference/statemachine-api/feature-bindings): reference
  `Trax.StateMachine.Gherkin.Reqnroll`, include the feature files, and configure the bindings with the host in the
  test process and a credential per person per scenario. There is no step code to write.

Nothing runs a feature through a page yet. A page's browser tests are written by hand, as the Bakery's
`BakeryPageTests` are: two people on pages of their own, telling the same story as the features. Nothing checks that
they still do when a scenario changes.

## SDK Reference

> [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines)
