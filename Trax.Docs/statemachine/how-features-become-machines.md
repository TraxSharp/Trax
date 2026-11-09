---
title: How features become machines
description: "How each glossary entry and Gherkin step becomes part of a machine's IR, its host file, its GraphQL fields and its C# builder equivalent."
type: explanation
area: machines
nav_order: 25
source:
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Persistence/FromIr
---

# How features become machines

A machine written as features passes through four forms: the glossary and the features a team writes, the
`<machine>.ir.json` and `host.json` that `trax machine from-gherkin` writes, the machine a host builds from them, and
the GraphQL fields and generated client a caller sees. This page follows one machine through all four, so that a
change in one place can be traced to the others. The how-to is
[Write machines as features](/docs/statemachine/write-machines-as-features), every key is in the
[feature files reference](/docs/reference/feature-files), and the reasons are
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md).

## One scenario, followed through

The CheckIn sample's passenger owns a booking. The glossary names the words:

```json
"booking": {
  "ownedBy": "passenger",
  "noun": "booking",
  "start": "has a booking",
  "initial": "Booked",
  "context": [
    { "name": "passport", "type": "string", "nullable": false, "constraints": [] },
    { "name": "seat", "type": "string", "nullable": true, "constraints": [] }
  ],
  "states": { "Open": "is open for check-in", "Issuing": "is getting a boarding pass" },
  "triggers": {
    "CheckIn": {
      "phrase": "checks in for {subject} in seat {seat}",
      "input": [ { "name": "seat", "type": "string", "nullable": false, "constraints": [] } ],
      "reduce": { "reduce": "set", "steps": [ { "field": "seat", "value": { "input": "seat" } } ] }
    }
  },
  "guards": {
    "hasPassport": {
      "holds": "has a passport number",
      "fails": "has no passport number",
      "rule": { "rule": "nonEmpty", "source": "context", "field": "passport" }
    }
  }
}
```

Two scenarios use them:

```gherkin
Scenario: A passenger checks in and gets a boarding pass
  Given Ada is a passenger
  And Ada has a booking
  And Ada's booking is open for check-in
  And Ada's booking has a passport number
  When Ada checks in for her booking in seat "14C"
  Then Ada's booking is getting a boarding pass

Scenario: Checking in with no passport number is refused
  Given Ada is a passenger
  And Ada has a booking
  And Ada's booking is open for check-in
  And Ada's booking has no passport number
  When Ada checks in for her booking in seat "14C"
  Then Ada is told "Add your passport number first."
  And Ada's booking is still open for check-in
```

Derivation reads the steps against the glossary's phrases. `Ada's booking` is the booking machine, because its
`noun` is `booking`, and it is Ada's instance. `checks in for her booking in seat "14C"` is the trigger `CheckIn`,
fired by Ada, with `14C` as its `seat` input. `has no passport number` is the guard `hasPassport` failing. The first
scenario shows `CheckIn` moving `Open` to `Issuing` when the guard holds. The second shows it refused when the guard
fails, with a message. Merged, they are one guarded edge, written to `booking.ir.json`:

```json
{ "from": "Open", "trigger": "CheckIn", "to": "Issuing",
  "guard": { "rule": "nonEmpty", "source": "context", "field": "passport" },
  "guardMessage": "Add your passport number first.",
  "reduce": { "reduce": "set", "steps": [ { "field": "seat", "value": { "input": "seat" } } ] } }
```

Ada fired `CheckIn` herself and Ada is a passenger, so passengers may fire it. That goes to `host.json`, which
clients never see:

```json
"triggers": { "CheckIn": { "roles": [["passenger"]] } }
```

It also goes to `access.txt`, the line a reviewer reads, as `booking CheckIn: passenger`. Served with
[`ServeFeatures`](/docs/sdk-reference/statemachine-api/add-state-machines), the trigger becomes the mutation
`machines { booking { checkIn(id, input, requestId) } }`, held to that rule. A caller who is not a passenger is
answered `FORBIDDEN`, and a booking with no passport number is answered with the guard's message.

## Where each part ends up

| Written as | In the IR | In `host.json` | Served as |
| --- | --- | --- | --- |
| a machine with `ownedBy` | the machine, by its id | `owner: "user"`, `start` and `read` rules | `machines.<machine>`: `start`, `get`, `discard` and the push |
| a machine with `system` and `name` | the machine | `owner: { "system": "<key>" }` | nothing a client calls: the host starts its one instance, and views read it |
| a machine with `system.keyedBy` | the machine | `owner: { "system": { "keyedBy": [...] } }` | nothing a client calls: host code starts and moves each instance |
| `states` | `states`, `initialState`, `context`, `committedStates`, `invariants` | | the state enum and context records of the generated client |
| `context`, `initialContext` | `context` (each state's), `initialContext` | | the context record |
| a trigger a person fires (`When Ada checks in for her booking in seat "14C"`) | `triggers`, a transition | `triggers.<T>.roles` (or `anyAuthenticated`) | the mutation `<trigger>` |
| a trigger no person fires (`When check-in opens for the flight`) | `triggers`, a transition | `triggers.<T>.server: true` | nothing: only the server fires it |
| a trigger only ever denied, or never fired | nothing | nothing | nothing |
| `triggers.<T>.input` and `reduce` | `inputs.<T>`, the transition's `reduce` | | the mutation's `input` |
| a guard, with a scenario where it holds and one where it fails | the transition's `guard` and `guardMessage` | | the refusal's message |
| `When 10 seconds pass` then a new state | `timers` (`<State>.after`) | | nothing: the server fires it |
| `invokes.<State>` | `outcomes` (`<State>.done`, `.failed`, `.cancelled`) with `output` | `invokes.<State>.train` and `input` | nothing: outcomes come from the run |
| `invokes.<State>.input` from `$owner` | nothing | `invokes.<State>.input` | nothing: the server sets it |
| a Then about another instance moving after a When | the moved machine's transition | `policies` (`linked`, `toSystem`, `broadcast`) | nothing: the server fires it |
| `links` | nothing | the `linked` policy's `link` field | nothing |
| `views` and a `show(s)` Then | nothing | `views`, with its readers | `views { <view> { ... } }` and `views.g.ts` |
| an effect on a trigger | the trigger, with its `keyPrefix` | `effects` | the effect event, sent with no input |
| `rules`, `reductions` | `custom` rules and reductions by name | `handlers` | |
| `version`, `migrations` | `version` | `migrations` | |
| `differential: true` | `differential`, from what the scenarios sent | | |
| every scenario | | `provenance`, by file, line and name | the console's Board tab |

Three rules hold across the table:

- **The IR is behaviour, `host.json` is everything else.** The IR ships to clients and its hash is in every persisted
  id, so it holds what any engine needs to move an instance and nothing about who may. Access, trains, policies,
  views, handler types and provenance are in `host.json` only.
- **What no scenario shows is not served.** A trigger, a view or a grant exists because a scenario shows it. A
  glossary entry no scenario uses derives nothing, and
  [`trax machine missing`](/docs/reference/cli#machines-from-features) lists it.
- **A person acts only on their own instance.** A step where Ada acts on Bo's booking, or on a system machine, is a
  derivation problem, not a grant.

## Who acts in a step

Every When has an actor, and the actor decides what derivation records:

| The When | Who fires it | What it grants |
| --- | --- | --- |
| `Ada checks in for her booking in seat "14C"` | Ada, on her own instance | her actor gets the trigger |
| `Ada's booking misses the flight`, `check-in opens for the flight` | the server | nothing: the trigger is server-only |
| `the boarding pass is issued` | the train the state invokes | nothing: an outcome is never a client's |
| `10 seconds pass` | the clock | nothing: a timer is never a client's |

A Then after the When says where each instance went. The first Then about the acting instance is where the When
took it. A later Then about another instance moving is a policy that the move set off, and derivation works out its
shape from whose instances they are: the same owner through a link is `linked`, a user's machine moving a system
machine is `toSystem`, and a system machine moving every user instance in a state is `broadcast`. A trigger a policy
fires is also server-only, and needs a scenario of its own showing it as a plain move.

## The same machine in C#

A machine served from features is the same machine a hand-written `[TraxMachine]` class builds: the host replays the
IR into the same builder and refuses to start if the result exports a different IR. For a reader who knows the
[builder](/docs/sdk-reference/statemachine-api/machine), each glossary entry is the call it stands for:

| Glossary | Builder |
| --- | --- |
| the machine's id | `Id("booking")` |
| `version` | `Version(2)` |
| `initial`, `initialContext` | `StartsAt(State.Booked, () => ...)` |
| `context`, a state's own `context` | `In(state).Context<TContext>()` |
| a state's `committed: true` | `In(state).Committed()` |
| a state's `requires` | `In(state).Requires(rule)` |
| `migrations` | `MigrateFrom(version, ...)` |
| `differential: true` | `Differential(d => ...)`, written from the scenarios |
| a trigger with `input` | `On(trigger).WithInput<TInput>()` |
| a trigger's `reduce` | `.Reduce(Set(...).FromInput(...))` |
| a guard's `rule`, its refusal's message | `.When(rule).Because("...")` |
| a scenario's move | `.To(state)` |
| `rules`, `reductions` | `CustomGuard(name, ...)`, `CustomReducer(name, ...)` |
| an effect on a trigger | `.RunsOnce<TEffect>(keyPrefix)` |
| `invokes.<State>` and its outcomes | `In(state).Invokes<TTrain, TInput, TOutput>(...)` with `OnDone`, `OnFailed`, `OnCancelled` |
| time passing in a scenario | `In(state).After(delay).GoTo(target)` |
| an actor shown firing a trigger | `.Requires("role")` on the transition |
| an actor shown starting one | `Start<TStartInput>(s => s.Requires("role"))` |
| who reads it | `Get(r => r.OwnerOnly().Requires("role"))`, the same for `Watch` and `Discard` |
| `system` | `SystemOwned()` |

Some of each side has no counterpart on the other:

- **Only from features:** server-only triggers (a hand-written machine has no public call for one), policies between
  machines, views, the started system instance at its fixed key, and provenance.
- **Only in C#:** spawning a child machine (`Spawns`, `Ends`, `Fails`), delegate guards and reducers, `RequiresPolicy`,
  `Anonymous`, `InvokedRunLimit`, and a timer whose duration a context field holds. A machine that needs one of
  these is written in C#. One host serves both kinds: `ServeFeatures` and the assemblies of `[TraxMachine]` classes go
  in the same `AddStateMachines` call.

## Checking that the forms agree

Each step checks the one before it, so a mismatch is found where it was made rather than at a client:

- `from-gherkin` replays every scenario on the machines it derived, access included, and writes nothing if one fails.
- `from-gherkin --check` in CI fails when the committed `machines/` no longer matches the features.
- The host refuses a `host.json` whose `schemaHash` is not the IR's, and a machine that does not export back to the
  IR it was built from.
- `trax machine run` plays the features on the committed files, and the
  [Reqnroll bindings](/docs/sdk-reference/statemachine-api/feature-bindings) play them against the running host.

`trax machine explain` prints what derivation read from each scenario: the edges, guards, timers and policies, and
which Then caused which. It is the quickest way to see why a part of the table above came out as it did.

## SDK Reference

> [ServeFeatures](/docs/sdk-reference/statemachine-api/add-state-machines) | [FeatureMachines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [IR format](/docs/sdk-reference/statemachine-api/ir-format) | [Machine](/docs/sdk-reference/statemachine-api/machine) | [Views root](/docs/sdk-reference/graphql-api/views)
