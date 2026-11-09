---
title: Feature files
description: "Reference for machines written as features: the glossary, the Gherkin steps derivation reads, what it refuses, host.json and access.txt."
type: reference
area: machines
nav_order: 9
---

# Feature files

What `trax machine from-gherkin` reads and writes. Why features are a source of machines, and the rules below, are
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md);
the how-to is [Write machines as features](/docs/statemachine/write-machines-as-features), and where each entry ends
up (the IR, `host.json`, GraphQL, the C# builder) is
[How features become machines](/docs/statemachine/how-features-become-machines). Both files are JSON with
schemas beside the IR's, in
[`Trax.StateMachine/conformance/`](https://github.com/TraxSharp/Trax/tree/main/Trax.StateMachine/conformance):
`glossary.schema.json` and `host.schema.json`. Every object is closed: a key the schema does not name is refused.

## The glossary

`glossary.json`, written by the team, beside the features.

| Key | What it is |
| --- | --- |
| `version` | `1`. |
| `actors` | Each actor by name: its `noun` (`a passenger`, as `Ada is a passenger` says it) and the `roles` a caller needs to be it, or `anyAuthenticated: true` for any signed-in caller. |
| `machines` | Each machine by id (kebab-case). |
| `views` | Each view by name: what it `reads` (a `machine`, and a system machine's `key`) and its `fields`, each `from` a machine's `state` or context field, a state with `labels` to show in its place. |

A machine is a user's or the system's:

| Key | What it is |
| --- | --- |
| `ownedBy` | The actor whose people own its instances, or a list of them (`["triage", "fixer"]`): each person owns their own instances whichever actor they are. With it, `noun` is how a step names one (`booking`, as `Ada's booking` says it) and `start` the phrase a person has one with (`has a booking`). |
| `system`, `name` | A system machine: its fixed key, which starts with the machine's id and a slash (`flight/tx101` for `flight`), and its name in a step (`the flight`). The host starts its one instance; only the server moves it. Or `{ "keyedBy": ["id"] }`, a keyed system machine: one instance per key, which host code starts and moves (see below). |
| `version` | The machine's definition version, which its snapshots carry. Defaults to `1`. |
| `initial` | The state an instance starts in. |
| `initialContext` | The context a new instance starts with. Without it, each field of the initial state's context starts at its zero, and a nullable one at `null`. |
| `context` | The fields of its context, as the IR declares fields: every state's, unless the state gives its own. No field, here or in a state's own context, may be named `state`, which a view reads as the machine's state. |
| `states` | Each state by name, with its phrase (`is booked`), or with `{ "phrase", "committed", "requires", "context" }`: `committed: true` (the IR's `committedStates`), the rule its context must meet (the IR's `invariants`), and a context of its own in place of the machine's. |
| `differential` | `true` to write the IR's `differential` block from the scenarios (below). |
| `rules`, `reductions` | Each custom rule and reduction the machine names, by its name in the IR: its C# `type` (an `ICustomRule` or `ICustomReduction` class) and the `example` an in-memory run answers with, whether the rule holds or the fields the reduction sets. |
| `migrations` | The C# `type` (an `IMachineMigration` class) of each step to the machine's `version`, by the version it steps from: one for every version below it, and none other. |
| `triggers` | Each trigger by name: its `phrase`, with `{subject}` where the instance is named and `{field}` for each of its `input` fields, and a `reduce` that sets context from the input. A phrase that starts with `{subject}` is something that happens to the instance, fired by no person. A `guard` names the guard that decides it when more than one tells its moves from its refusals, and an `effect` makes it a send (below). |
| `guards` | Each guard by name: its `rule`, and the `holds` and `fails` phrases a Given sets it up with. A guard whose rule reads only the trigger's input leaves the phrases out: a When's value shows it, as in `When Ada answers "" on her answer sheet`. |
| `invokes` | Each state that runs a train: the `train`'s interface full name, its `input` from context fields (or from `$owner`, below), its `output` fields, its `outcomes` by phrase (`done`, `failed` or `cancelled`, a `when` rule on the output, a `reduce`), and `examples`, the output an in-memory run answers with. |
| `links` | Each link by its phrase: the other user machine (`machine`) whose instance this machine's context `field` holds the id of. A `linked` policy finds its target through it (below). |

Fields, rules and reductions are written as the IR writes them, so a glossary uses the IR's vocabulary rather than
one of its own:

- A field in `context`, a trigger's `input` or an invoked train's `output` is
  `{ "name", "type", "nullable", "constraints" }`, with the optional keys the IR's schemas take
  ([the IR format](/docs/sdk-reference/statemachine-api/ir-format#top-level)). An integer in an invoked train's
  `output`, a dictionary's `values` among them, carries its CLR type's exact range as `minimum` and `maximum`
  (`-2147483648` and `2147483647` for an `int`), since the host checks the IR against the train when it starts and
  names any range that differs.
- A guard's `rule`, a state's `requires`, an outcome's `when` and a field's `constraints` are IR rules, tagged by
  `rule`: `{ "rule": "nonEmpty", "source": "context", "field": "passport" }`. The kinds and their
  keys are in [the IR format's transitions](/docs/sdk-reference/statemachine-api/ir-format#transitions), and the C#
  each one exports from is in [Rules](/docs/sdk-reference/statemachine-api/rules).
- A trigger's or an outcome's `reduce` is an IR reduction, tagged by `reduce`:
  `{ "reduce": "set", "steps": [ { "field": "passport", "value": { "input": "passport" } } ] }`, or `keep`, `clear`,
  `reset` and `custom`.

### Links

A policy between two machines of one owner needs to know which instance it moves, since a customer can hold several
of each. A link says so: the target machine holds the source instance's id in a context field.

```json
"receipt": {
  "ownedBy": "customer",
  "noun": "receipt",
  "start": "has a receipt",
  "initial": "Issued",
  "context": [ { "name": "orderId", "type": "string", "nullable": true, "format": "uuid", "constraints": [] } ],
  "states": { "Issued": "is issued", "Void": "is void" },
  "triggers": { "Cancel": { "phrase": "{subject} is voided" } },
  "links": { "the receipt for {subject}": { "machine": "order", "field": "orderId" } }
}
```

```gherkin
Scenario: An expired order voids its receipt
  Given Ada's order is placed
  When Ada lets her order expire
  Then Ada's order has expired
  And Ada's receipt is void
```

Derived, this is a `linked` policy: when an order enters `Expired`, the server fires `Cancel` on the receipt of the
same owner whose `orderId` is that order's id. Without the link the policy is refused, since the same owner alone
does not say which receipt. A receipt with no `orderId`, or another order's, is not moved. `Cancel` becomes
server-only, and the scenario `Given Ada's receipt is issued` / `When Ada's receipt is voided` shows it as a plain
move. The field is set like any other context field, by a trigger's `reduce` from its input.

A keyed system machine (`"system": { "keyedBy": ["id"] }`) has one instance per key, such as one per ticket. The
host starts none of them: host code starts and moves each one through
[`IMachineInstances`](/docs/sdk-reference/statemachine-api/machine-instances) by the machine's id and the key
(`Start("ticket", "T-12")`). Its trigger phrases say where the instance goes as `{subject}` (`{subject} gets triaged`,
said `ticket "T-12" gets triaged`), and every trigger is the server's. No view reads one, no policy moves one or is set
off by one, and no person fires one: each is refused.

A train that reads who it is acting for takes them as input: `"input": { "memberId": "$owner" }` gives the train's
`memberId` the instance owner's qualified principal id (`{scheme}:{id}`, the owner's user key). The server sets it when
the state is entered; it is in no context, so no client writes it, and a save that puts it in the context is refused as
any unknown field is. A system machine has no owner, so `$owner` there is refused, and so is a context field named
`$owner`. Derivation copies the mapping to `host.json`.

## The steps

A scenario names its people, never "I". The step's own person is `her`, `his` or `their`.

| Step | Means |
| --- | --- |
| `Given Ada is a passenger` | Ada is a person of that actor. |
| `Given Ada has a booking` | Ada starts an instance of her own. |
| `Given Ada's booking is checked in`, `Given the flight is closed`, `Given ticket "T-12" is new` | The instance is in that state. Arranged, not acted: it fires nothing. A keyed system machine's instance is named by its name and each part of its key, quoted. |
| `Given Ada's booking has a passport number` | The guard holds (or, with its `fails` phrase, fails): its context is set so. |
| `When Ada checks in for her booking in seat "14C"` | Ada fires the trigger on her instance, which grants her actor the trigger. |
| `When Ada's booking misses the flight`, `When check-in opens for the flight` | The server fires it: no person is granted it. |
| `When the boarding pass is issued` | The train the state invokes ends with that outcome. |
| `When 5 seconds pass` | Time passes (`seconds`, `minutes`, `hours`, `days`); a timer that falls due fires. |
| `Then Ada's booking is checked in`, `Then Ada's booking is still open for check-in` | Where the instance is. A When's Thens that name other instances moving are the policies the move sets off. |
| `Then Ada is told "Add your passport number first."` | The When was refused by a guard, with that message. |
| `Then Ada is not allowed` | Ada's actor may not fire the When's trigger: it is not served to her. |
| `Then Ada's bookings show status "Passport not accepted"` | What one field of a view shows Ada: her own instances and the system's. |

Derivation refuses a data table, a DocString, a `*` step, a first-person step, a step two phrases match, a Given or
When no phrase matches, and two scenarios that disagree. A Then no phrase matches is about the UI: it is reported and
left alone, and `trax machine run --strict` fails on it.

## How a scenario is read

Derivation walks each scenario step by step and reads its shape as well as its words. These are the rules a scenario
has to follow; `trax machine explain` prints what derivation read from each one.

| Rule | Write | Otherwise |
| --- | --- | --- |
| A When is followed by a Then about the instance it moved, before the next When. | `When Ada checks in for her booking in seat "14C"` / `Then Ada's booking is getting a boarding pass` / `When the boarding pass is issued` | "Nothing after this When says what it did." |
| An outcome's first Then names the state it took the instance to, and an outcome goes to one state in every scenario. | `When the boarding pass is issued` / `Then Ada's booking is checked in` | "Say where 'the boarding pass is issued' takes Ada's booking", or "goes to NotIssued here and to CheckedIn elsewhere". |
| The Thens after a When follow the cascade in order. The first about the acting instance says where the When took it; one naming it in another state, after another instance moved, says where a policy took it on, and is caused by the instance that moved just before it. A run reads each instance's Thens in the order it entered those states during this When; a `still` Then reads only where it is. | `When Kit opens his desk` / `Then Kit's desk is open` / `And the flight is open for check-in` / `And Ada's booking is open for check-in` | Naming it in two states with no other instance moving between: "The Thens after this When disagree about what it did to Kit's desk." A run fails a Then naming a state out of that order, or one only an earlier When passed through. |
| A later Then about another instance moving is a policy the move set off. A user's machine moving the system's is `toSystem`, the system's moving every user instance in a state is `broadcast`. | `When Kit closes the gate at his desk` / `Then Kit's desk is closing the gate` / `And the flight is closed` / `And Ada's booking missed the flight` | Nothing: it is derived as a policy. Check it with `trax machine explain`. |
| A trigger a policy fires also has a scenario of its own, showing it as a plain move. | `Given the flight is scheduled` / `When check-in opens for the flight` / `Then the flight is open for check-in` | "flight goes from Scheduled to Open when desk enters Open, and no scenario shows a trigger doing that." |
| Two timers falling due at the same moment read as one moving the other. Give them different durations, or show them in different scenarios. | `When 2 seconds pass` / `Then Kit's desk is done` / `And the flight is still closed` | "flight goes from Closed to Departed when desk enters Done". |
| `still` after time passing says the timer has not fallen due; after a trigger it says the trigger was refused. | `When 10 seconds pass` / `Then Ada's booking is still checked in` | Two different facts from one word: read it as the scenario's When decides. |
| A Then naming the state the trigger left from is a self-transition: it is served, the instance stays in its state and sets no policy off, and the trigger's `reduce` still applies. Use it where a policy may land on a machine already there. | `Given the flight is open for check-in` / `When check-in opens for the flight` / `Then the flight is open for check-in` | Without it the policy's fire is refused and logged each time. |
| A trigger only some roles may fire does not lead back to the state an instance starts in: whoever may start one could put it there anyway. | Lead it to a state of its own (`Open --CloseGate--> Closing`). | "the rule would gate nothing, since whoever may start one can put it there." |
| Timers alone do not form a loop. Let an event lead out of one of the states. | `Closed --after 4 s--> Departed`, `Departed --after 10 s--> Scheduled`, `Scheduled --OpenCheckIn--> Open` | "the timers of Closed -> Departed -> Scheduled -> Closed form a cycle no event breaks", at each scenario that showed one of them; the host refuses the same loop. |
| A person fires triggers on their own instances only, and never on a system machine. | `When check-in opens for the flight` (the server), not `When Kit opens check-in for the flight`. | "only the server moves a system machine", or "acts on Ada's booking". |

The order of the Thens is not an order on the host. In memory, `trax machine run` plays a When's whole cascade at
once, in the order its Thens list it. The host runs each policy fire and each invoked train on its own, in no fixed
order: the policy runner works the fires every `PolicyRunnerOptions.Interval` (1 second by default) and a worker runs
the train on its own poll. So a policy that moves instances out of a state another policy moves them into, when its
own state is reached from the other's with no person in between (through a train's outcome, another policy, or a timer
shorter than the runner's default interval of 1 second), may run first and find none there. The same holds when the
earlier policy's instances reach that state only after a train's outcome or a short timer moves them on. Derivation warns of such a
pair, naming both policies, and writes the machines all the same (`trax machine from-gherkin` prints the warnings, and
`FeatureMachines.Derive` returns them as `Warnings`): move the instances from a state the earlier policy leaves alone,
or move them in one policy. A timer of 1 second or more, or one whose duration a context field holds, spaces the two
policies, and they are not warned of. Derivation cannot see a host's options, so it measures against the default: a
host that sets `Interval` above 1 second can let a timer shorter than its interval fire first with no warning. Keep
timers between such policies longer than the interval you set.

## The C# a machine names

The glossary names the C# behind a machine, and derivation writes what a client sees into the IR (the custom names,
an effect's `keyPrefix`) and the types into `host.json`, which a host pairs one for one with the IR.

- An `effect` on a trigger: its `type` (an interface extending `ISnapshotEffect`), its `keyPrefix` (`<machine>:<Trigger>`
  when not given) and the receipt `example` an in-memory run answers with. A machine runs one. The trigger is a send:
  its owner sends it with no input of theirs, so its phrase holds no `{field}`, and the server runs the effect and fires
  the trigger with the receipt, its only input (`receipt`, a string). `When Ada pays for her checkout` is a send.
- Every custom rule and reduction the machine names has its `rules` or `reductions` entry, and every entry is named.

Derivation and `trax machine run` load no assembly, so they play each handler's `example`. A scenario that needs the
real handler (two items cost twice one) is tagged `@host`: derivation reads its steps and nothing more, an in-memory
run lists it as skipped, and the host bindings run it.

## The differential block

With `differential: true`, derivation writes the IR's `differential` block from what the scenarios did, so the
engines can be held to each other on the machine (the corpus `trax machine generate --corpus-out` writes from it):

| Key | From |
| --- | --- |
| `samples.<Trigger>` | Every input a scenario sent the trigger with, a trigger served to someone. |
| `samples.<State>.done` | The `example` of every `done` outcome a scenario gave. |
| `seeds.<State>` | Every context a Given put an instance in before a When fired from it, but the initial one. |

Each list holds each value once, in canonical order, so the block (and the IR's hash) moves only when a scenario
changes what it sends. Nothing writes `contexts`, a probe of contexts no scenario reaches: a value a scenario cannot
send, a hostile one, is for a machine written in C#. A machine that asks for a block and whose scenarios give nothing
to put in it is a problem.

## Tags

One tag means something to Trax: `@host`, on a scenario that needs a real handler. Derivation reads its steps, an
in-memory run skips it, and the host bindings run it. Every other tag is yours, for `trax machine run --tags` and for
Reqnroll hooks. The CheckIn sample's `@in-memory` is one of those: a hook in its test project skips those scenarios on
the host, because the host's boarding pass train never fails by itself. Trax does nothing with it.

## Problems derivation reports

Each problem names the file and line of the step or scenario it is about. The common ones, and what to change:

| The problem says | Change |
| --- | --- |
| No phrase in the glossary says '...' | A Given or When the glossary does not read. Add the phrase to the glossary, or reword the step to one it has. |
| '...' could mean ... | Two phrases match the step. Reword one of them. |
| '...' speaks as 'I'. Name the person | Write `Ada ...` after `Given Ada is a passenger`. |
| '...' names Ada, and the scenario never says who Ada is | Add `Given Ada is a passenger` before the step. |
| Nothing after this When says what it did | Add `Then Ada's booking is ...` straight after the When. |
| Say where '...' takes Ada's booking | An outcome's When needs a Then naming the state it reached. |
| ... goes to Issuing here and to CheckedIn in '...' | Two scenarios disagree on one trigger from one state. Fix the wrong one, or add a guard that tells them apart. |
| ... is refused here and works in '...', and no guard in the glossary tells the two apart | Give the two scenarios Givens of a guard's `holds` and `fails` phrases. |
| ... is the message of a refusal, and no scenario shows ... working | Add a scenario where the trigger is allowed, so there is a guard to carry the message. |
| ... acts on Bo's booking | A person acts only on their own instance. Have the other person act, or make it the server's step. |
| ... only the server moves a system machine | Write it without a person: `When check-in opens for the flight`. |
| ... is fired by Ada here and by no one in '...' | A person and the server cannot share a trigger. Make them two triggers. |
| ... is fired by a person in another scenario and by a policy here | Same: two triggers, one for people and one for the policy. |
| ... moves when ... does, and the same owner alone does not say which ... | Add a [link](#links). |
| ... moves when ... does. A policy acts on the same owner's instances | Another owner's instance needs shared instances, which Trax does not have yet. |
| ... no scenario shows a trigger doing that | A trigger a policy fires also needs a scenario of its own showing it as a plain move. |
| ... leaves ... with no time passing there | Add `When 5 minutes pass` before the Then that shows it leaving. |
| the timers of ... form a cycle no event breaks | Let a trigger lead out of one of the states. |
| ... invokes ..., and no scenario says where a failed run takes it | Show each of `done`, `failed` and `cancelled`. If the real train never produces one by itself, show it anyway and skip that scenario on the host with a tag of your own, as the CheckIn sample does. |
| ... an outcome's state is reserved for what the train produced | Send the outcome to a state of its own. |
| The derived machine answers this differently | The scenario fails on what was derived from all the scenarios together. Run `trax machine explain` on it. |

A Then the glossary does not read is not a problem: it is about the UI, listed with the run, and fails only under
`trax machine run --strict`.

## host.json

Written by `from-gherkin`, never by hand. Everything only the server reads:

| Key | What it is |
| --- | --- |
| `machines` | Each machine's `schemaHash` (the IR it was derived with), `owner` (`user`, `{ "system": "<key>" }`, or `{ "system": { "keyedBy": [...] } }` for a keyed system machine), who may `start` and `read` it, each trigger's rule (`roles`, `anyAuthenticated`, or `server`), the train each invoking state runs, with its input from context fields or `$owner`, the `effects` each effect-bound trigger runs, the `handlers` of its custom `rules` and `reductions`, and its `migrations` by the version each steps from, each a type's full name. A trigger with no entry is not served. |
| `policies` | The server acting when a machine enters a state: `linked` (the same owner's instance through a link), `broadcast` (from a system machine, every instance waiting in a state) or `toSystem` (a user's machine moving the system's at its key). |
| `views` | Each view a scenario reads, its fields and its readers. A view no scenario reads is not served. |
| `provenance` | For each part of each machine, the scenarios that produced it, by file, line and name, never by step text. The console's Board tab shows them. |

A host refuses a directory whose IR the host file was not derived from, or whose names disagree with it: among them an
effect, custom rule, custom reduction or version step of the IR with no type, or a type for one the IR does not have. It also
refuses policies that set each other off with no timer between them, as derivation does, through the same check: a
policy fires a trigger, the trigger can move its machine into a state, a policy waiting for that state fires next, and a
state that runs a train counts as moving on to wherever its outcomes go. A loop of those moves alone is refused, naming
its policies. A loop that passes through a timer is paced by it and allowed, so a flight that a timer schedules again
after it departs can still have policies on its opening and its closing.

## access.txt

One sorted line per grant, committed and reviewed: each actor's roles (`@passenger: roles passenger`), then each
machine's `start`, `read` and trigger, and each view, with the actors its rule admits. A machine several actors own
grants each trigger to the owning actors the scenarios show firing it (`assignment Triage: triage`,
`assignment SubmitFix: fixer`), `start` to those shown starting one, and `read` to all of them; each still acts on their
own instances only. A rule whose roles would also admit an owning actor a scenario shows refused (`Fay is not
allowed`) is a derivation problem, never a wider grant. An actor a rule admits by its
roles that no scenario shows is marked `(no scenario)`. `from-gherkin --check` fails when it is stale, so a new grant
is a line in the pull request.

## SDK Reference

> [ServeFeatures](/docs/sdk-reference/statemachine-api/add-state-machines) | [FeatureMachines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [IR format](/docs/sdk-reference/statemachine-api/ir-format) | [Rules](/docs/sdk-reference/statemachine-api/rules)
