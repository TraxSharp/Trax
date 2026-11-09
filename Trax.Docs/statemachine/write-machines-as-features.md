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
The [CheckIn sample](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/CheckIn/README.md) is this page,
built, and [How features become machines](/docs/statemachine/how-features-become-machines) follows each glossary
entry and step to the IR, `host.json`, GraphQL and the C# builder.

## Write the glossary

The glossary names the words the features use: the actors and the roles they carry, each machine with its states,
triggers and guards, and the views. A user's machine is owned by an actor; a system machine lives at a fixed key.

```json
{
  "version": 1,
  "actors": { "passenger": { "noun": "a passenger", "roles": ["passenger"] } },
  "machines": {
    "booking": {
      "ownedBy": "passenger",
      "noun": "booking",
      "start": "has a booking",
      "initial": "Booked",
      "context": [ { "name": "passport", "type": "string", "nullable": false, "constraints": [] } ],
      "states": { "Booked": "is booked", "Open": "is open for check-in" },
      "triggers": {
        "GivePassport": {
          "phrase": "gives passport {passport} for {subject}",
          "input": [ { "name": "passport", "type": "string", "nullable": false, "constraints": [] } ],
          "reduce": { "reduce": "set", "steps": [ { "field": "passport", "value": { "input": "passport" } } ] }
        }
      }
    }
  }
}
```

Every key is in the [feature files reference](/docs/reference/feature-files#the-glossary).

## Write the features

Each scenario names its people in the third person, sets up what it needs with Givens, acts with Whens, and says what
happened with Thens:

```gherkin
Feature: Checking in for a flight
  Scenario: A passenger gives a passport number
    Given Ada is a passenger
    And Ada has a booking
    And Ada's booking is open for check-in
    When Ada gives passport "X1234567" for her booking
    Then Ada's booking is open for check-in
```

An actor's own act grants that actor the trigger. A trigger no scenario shows anyone firing is not served at all. The
steps derivation reads are in the [reference](/docs/reference/feature-files#the-steps); a Then it does not read is
about the UI, and is left alone.

## Add guards, trains, timers, policies and views

Each of these is a glossary entry plus the scenarios that show it. The examples are the CheckIn sample's.

**A guard** is a pair of phrases and the rule they stand for. A scenario where it holds and one where it fails, with
the message, make a guarded move:

```json
"guards": {
  "hasPassport": {
    "holds": "has a passport number",
    "fails": "has no passport number",
    "rule": { "rule": "nonEmpty", "source": "context", "field": "passport" }
  }
}
```

```gherkin
Scenario: Checking in with no passport number is refused
  Given Ada's booking is open for check-in
  And Ada's booking has no passport number
  When Ada checks in for her booking in seat "14C"
  Then Ada is told "Add your passport number first."
  And Ada's booking is still open for check-in
```

**A train** runs when an instance enters a state. The glossary names its interface, its input from context fields,
its output, and a phrase for each outcome, with the output an in-memory run answers with:

```json
"invokes": {
  "Issuing": {
    "train": "Trax.Samples.CheckIn.Trains.IIssueBoardingPassTrain",
    "input": { "passport": "passport" },
    "output": [ { "name": "accepted", "type": "boolean", "nullable": false, "constraints": [] } ],
    "outcomes": {
      "the boarding pass is issued": { "outcome": "done", "when": { "rule": "boolEquals", "source": "input", "field": "accepted", "value": true } },
      "the passport is turned down": { "outcome": "done", "when": { "rule": "boolEquals", "source": "input", "field": "accepted", "value": false } },
      "the boarding pass cannot be issued": { "outcome": "failed" },
      "issuing the boarding pass is called off": { "outcome": "cancelled" }
    },
    "examples": {
      "the boarding pass is issued": { "accepted": true },
      "the passport is turned down": { "accepted": false }
    }
  }
}
```

```gherkin
When Ada checks in for her booking in seat "14C"
Then Ada's booking is getting a boarding pass
When the boarding pass is issued
Then Ada's booking is checked in
```

Every invoking state needs a scenario for each of `done`, `failed` and `cancelled`. The train is the one piece of
C# behind the machine, written as in [Invoke a train from a state](/docs/statemachine/invoking-trains).

**A timer** is time passing in a scenario. There is no glossary entry:

```gherkin
Scenario: A gate agent steps away once the gate is closed
  Given Kit is a gate agent
  And Kit has a desk
  And Kit's desk is closing the gate
  And the flight is closed
  When 2 seconds pass
  Then Kit's desk is done
  And the flight is still closed
```

**A policy** is a Then about another instance moving after a When. A system machine has a fixed key and a name, and
only the server moves it:

```json
"flight": {
  "system": "flight/tx101",
  "name": "the flight",
  "initial": "Scheduled",
  "states": { "Scheduled": "is scheduled", "Open": "is open for check-in", "Closed": "is closed", "Departed": "has departed" },
  "triggers": {
    "OpenCheckIn": { "phrase": "check-in opens for the flight" },
    "CloseGate": { "phrase": "the flight closes its gate" }
  }
}
```

```gherkin
Scenario: A gate agent opens check-in for everyone booked
  Given Ada is a passenger
  And Ada has a booking
  And Kit is a gate agent
  And Kit has a desk
  And the flight is scheduled
  When Kit opens his desk
  Then Kit's desk is open
  And the flight is open for check-in
  And Ada's booking is open for check-in
```

That derives a `toSystem` policy: when a desk enters `Open`, the server fires `OpenCheckIn` on `flight/tx101`. The
flight entering `Open` then moves every booking still `Booked`, which is a `broadcast`. Each machine's
`OpenCheckIn` also needs a scenario of its own showing it as a plain move (`When check-in opens for the flight`,
`When Ada's booking opens for check-in`). Two machines of the same owner need a
[link](/docs/reference/feature-files#links).

**A view** is what a reader sees across machines, with labels for states. A scenario that reads it makes its actor a
reader, and a view no scenario reads is not served:

```json
"views": {
  "bookings": {
    "reads": [ { "machine": "booking" } ],
    "fields": {
      "status": { "from": "booking.state", "labels": { "CheckedIn": "Checked in", "TurnedDown": "Passport not accepted" } },
      "seat": { "from": "booking.seat" }
    }
  }
}
```

```gherkin
Then Ada's bookings show status "Passport not accepted"
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
    .AddStateMachines(o => o.ServeFeatures("machines", typeof(IssueBoardingPassTrain).Assembly))
    .AddMediator(typeof(IssueBoardingPassTrain).Assembly)
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

Nothing runs a feature through a page yet. A page's browser tests are written by hand, as the CheckIn sample's
`CheckInPageTests` are: a gate agent and three passengers on pages of their own, telling the same story as the
features. Nothing checks that
they still do when a scenario changes.

## SDK Reference

> [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines)
