---
title: Write machines as features
description: "Write machines as Gherkin features and a lexicon, derive them with trax machine from-gherkin, serve them, and run the features in memory and against the host."
type: how-to
area: machines
nav_order: 24
---

# Write machines as features

A team that describes what happens in Gherkin can have Trax derive the machines from it: the states, the moves, the
guards, the timers, the policies between machines, the views, and who may do what. Nothing is written in C# but the
trains the machines invoke. Why this is a source of machines, and what it refuses, is
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md),
and why the words are written as a lexicon that Trax turns into the glossary is
[ADR 0059](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0059-the-lexicon-is-the-source-and-the-glossary-is-derived.md).
The [CheckIn sample](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/CheckIn/README.md) is this page,
built, and [How features become machines](/docs/statemachine/how-features-become-machines) follows each lexicon
line and step to the IR, `host.json`, GraphQL and the C# builder.

## Write the lexicon

The lexicon names the words the features use: the actors and the roles they carry, each machine with its states,
triggers and guards, and the views. It is one file beside the features, `features/<name>.lexicon`, holding every
machine, and each line starts with a keyword. A user's machine is owned by an actor; a system machine lives at a fixed
key.

```text
Version: 1

Actor: passenger "a passenger"
  Roles: passenger

Machine: booking
  Owned by: passenger
  Started by: "has a booking"

  State: Booked  "is booked"
  State: Open    "is open for check-in"

  Trigger: GivePassport "gives passport {passport} for {subject}"
    Input: passport is a string
    Sets: passport
```

The machine's noun is its id (`booking`, as `Ada's booking` says it), it starts in its first state, and its context
holds `passport` because `Sets:` writes it. Every keyword, and what is filled in when a line leaves it out, is in the
[lexicon reference](/docs/reference/lexicon). `trax lexicon format` lines up its columns.

Trax elaborates the lexicon to the glossary, the JSON every engine reads, and `from-gherkin` writes it as
`glossary.json` beside the machines (below). Its keys are in the
[feature files reference](/docs/reference/feature-files#the-glossary). An app whose features still have a hand-written
`glossary.json` passes it with `--glossary`; a lexicon and a `glossary.json` in one features directory are refused.

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

Each of these is a few lexicon lines plus the scenarios that show it. The examples are the CheckIn sample's.

**A guard** is a pair of phrases and the rule they stand for. A scenario where it holds and one where it fails, with
the message, make a guarded move:

```text
  Guard: hasPassport when passport is not empty
    Holds: "has a passport number"
    Fails: "has no passport number"
```

```gherkin
Scenario: Checking in with no passport number is refused
  Given Ada's booking is open for check-in
  And Ada's booking has no passport number
  When Ada checks in for her booking in seat "14C"
  Then Ada is told "Add your passport number first."
  And Ada's booking is still open for check-in
```

**A train** runs when an instance enters a state. The lexicon names its interface and the state, its input from
context fields (`Takes:`), its output (`Returns:`), and a phrase for each outcome. A done outcome's `when` is also the
output an in-memory run answers with:

```text
  Runs: Trax.Samples.CheckIn.Trains.IIssueBoardingPassTrain in Issuing
    Takes: passport
    Returns: accepted is a boolean
    Outcome: "the boarding pass is issued"              when accepted is true
    Outcome: "the passport is turned down"              when accepted is false
    Outcome: "the boarding pass cannot be issued"       when it fails
    Outcome: "issuing the boarding pass is called off"  when it is called off
```

Given the trains' assemblies (`from-gherkin --trains bin/Trains.dll`), `Takes:` and `Returns:` may be left out: they
are read from the train's input and output records, as the host reads them, and a `Returns:` line that disagrees with
the record is a problem at its line.

```gherkin
When Ada checks in for her booking in seat "14C"
Then Ada's booking is getting a boarding pass
When the boarding pass is issued
Then Ada's booking is checked in
```

Every invoking state needs a scenario for each of `done`, `failed` and `cancelled`. An outcome may not go to the
state the machine starts in (unless that state invokes a train itself): an outcome's state is reserved for what the
train produced, so no instance could be started there. Send it to a state of its own, with a move back if the flow
returns. The initial state may invoke a train: starting an instance queues its run. The train is the one piece of
C# behind the machine, written as in [Invoke a train from a state](/docs/statemachine/invoking-trains). Its input and
output records' properties are the lexicon's field names in camelCase (`Accepted` is `accepted`).

A done outcome keeps what the train produced with a `Stores:` line under `Runs:`, which reads fields of the train's
output into the context, renamed with `as`:

```text
    Stores: result as lastShot
```

On a machine a person owns, an outcome may not go to another state that invokes a train. The server would queue that
run with nobody there to run it as, so derivation refuses it, as it refuses a timer or a policy that enters one. Stop
in a state of the outcome's own and let a trigger the person fires go on from there, or have one train do both steps.
Only a `system` machine chains runs through outcomes.

**A timer** is time passing in a scenario. There is no lexicon line:

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

```text
Machine: flight "the flight"
  System at: flight/tx101
  State: Scheduled  "is scheduled"
  State: Open       "is open for check-in"
  State: Closed     "is closed"
  State: Departed   "has departed"
  Trigger: OpenCheckIn  "check-in opens for the flight"
  Trigger: CloseGate    "the flight closes its gate"
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
flight entering `Open` then moves every booking still `Booked`, which is a `broadcast`. A broadcast moves the
instances in exactly one state: a Then that shows instances moving out of two states from one transition is refused,
so give them one state to wait in. Each machine's
`OpenCheckIn` also needs a scenario of its own showing it as a plain move (`When check-in opens for the flight`,
`When Ada's booking opens for check-in`). Two machines of the same owner need a
[link](/docs/reference/feature-files#links).

**A view** is what a reader sees across machines, with labels for states. A scenario that reads it makes its actor a
reader, and a view no scenario reads is not served:

```text
View: "bookings"
  Reads: booking
  Field: status from booking's state
    Label: CheckedIn   "Checked in"
    Label: TurnedDown  "Passport not accepted"
  Field: seat from booking's seat
```

```gherkin
Then Ada's bookings show status "Passport not accepted"
```

## Derive the machines

```bash
trax machine from-gherkin --features features --out machines
```

It reads the lexicon in `features`, and writes one `<machine>.ir.json` per machine, `host.json` with what only the
server reads, `access.txt`, one line per grant, and `glossary.json`, the lexicon elaborated, so a reviewer sees what
was filled in. It replays every scenario on what it derived, and refuses features that contradict each other, or that
would derive a machine the host refuses, naming the file and line. Commit `machines/`, and review `access.txt` in every
pull request: a new grant shows there. `trax machine from-gherkin --check` in CI fails when a committed file is stale.

`trax machine missing --features features` lists what the lexicon names and no scenario shows, each with a scenario
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
disagree refuses the host at startup. Copy `machines/` into the build output, register an
[`ISnapshotPrincipal`](/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotprincipal), which says whose
instances a request reaches and which Trax does not register for you, and export the client manifest and run
`trax client` as for any machine. [Serve features from the hub template](/docs/statemachine/serve-features-from-the-hub-template)
takes a `trax-hub` project through each step, persisted operations and the timer and policy intervals included.

A state that invokes a train only queues its run. `AddScheduler()`, on this host or another sharing the database, runs
it, and without one the instance waits in that state. See
[Meet the host's requirements](/docs/statemachine/invoking-trains#3-meet-the-hosts-requirements).

## Run the features

- **In memory**, on the committed machines: `trax machine run --features features --machines machines`, or
  [`FeatureMachines.Run`](/docs/sdk-reference/statemachine-api/feature-machines) from a test. Each answer is the
  host's, access included.
- **Against the host**, with [Reqnroll](/docs/sdk-reference/statemachine-api/feature-bindings): reference
  `Trax.StateMachine.Gherkin.Reqnroll`, include the feature files, and configure the bindings with the host in the
  test process, the glossary the lexicon gives
  ([`Lexicon.ReadDirectory`](/docs/sdk-reference/statemachine-api/feature-machines#lexicon)), and a credential per
  person per scenario. A machine feature needs no step code. What the machines do not cover goes in specs under
  `specs/`, which derivation does not read, with the few steps the lexicon's phrases do not read registered beside the
  bindings.

Nothing runs a feature through a page yet. A page's browser tests are written by hand, as the CheckIn sample's
`CheckInPageTests` are: a gate agent and three passengers on pages of their own, telling the same story as the
features. Nothing checks that
they still do when a scenario changes.

## What features cannot say yet

Some designs need something the lexicon has no word for. Each of these is a limit of what is served today:

- **A policy carries no data.** A policy fires a trigger with no input on the instances it reaches, so a value in
  the moving instance's context does not travel with it. Put the value where the target can read it, or have a train
  write it.
- **A view does not show a timer's `dueAt`.** A view's field is a state or a context field. A user's own instance
  carries `dueAt` on its machine's `get` and push ([Machines root](/docs/sdk-reference/graphql-api/machines)), but a
  system machine read through a view does not, so a page cannot count down to its timer on the server's clock.
- **The server has no supported read of other users' instances.** A view shows the caller's own instances and the
  system's. `IMachineInstances` starts and moves system instances, and the operations service lists instances by
  machine and state without their context or owner. A train that needs every user's instances (to score every
  answer, say) reads the snapshot table through `IDataContext.SnapshotDrafts` itself, and is then tied to how the
  features name machines, states and fields: renaming one in the lexicon breaks the train, and derivation does not
  notice.

## SDK Reference

> [Lexicon](/docs/reference/lexicon) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines)
