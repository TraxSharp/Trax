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
the how-to is [Write machines as features](/docs/statemachine/write-machines-as-features). Both files are JSON with
schemas beside the IR's, in
[`Trax.StateMachine/conformance/`](https://github.com/TraxSharp/Trax/tree/main/Trax.StateMachine/conformance):
`glossary.schema.json` and `host.schema.json`. Every object is closed: a key the schema does not name is refused.

## The glossary

`glossary.json`, written by the team, beside the features.

| Key | What it is |
| --- | --- |
| `version` | `1`. |
| `actors` | Each actor by name: its `noun` (`a customer`, as `Ada is a customer` says it) and the `roles` a caller needs to be it, or `anyAuthenticated: true` for any signed-in caller. |
| `machines` | Each machine by id (kebab-case). |
| `views` | Each view by name: what it `reads` (a `machine`, and a system machine's `key`) and its `fields`, each `from` a machine's `state` or context field, a state with `labels` to show in its place. |

A machine is a user's or the system's:

| Key | What it is |
| --- | --- |
| `ownedBy` | The actor whose people own its instances, or a list of them (`["triage", "fixer"]`): each person owns their own instances whichever actor they are. With it, `noun` is how a step names one (`order`, as `Ada's order` says it) and `start` the phrase a person has one with (`has an order`). |
| `system`, `name` | A system machine: its fixed key (`shop/main`) and its name in a step (`the shop`). The host starts its one instance; only the server moves it. Or `{ "keyedBy": ["id"] }`, a keyed system machine: one instance per key, which host code starts and moves (see below). |
| `version` | The machine's definition version, which its snapshots carry. Defaults to `1`. |
| `initial` | The state an instance starts in. |
| `initialContext` | The context a new instance starts with. Without it, each field of the initial state's context starts at its zero, and a nullable one at `null`. |
| `context` | The fields of its context, as the IR declares fields: every state's, unless the state gives its own. |
| `states` | Each state by name, with its phrase (`is a draft`), or with `{ "phrase", "committed", "requires", "context" }`: `committed: true` (the IR's `committedStates`), the rule its context must meet (the IR's `invariants`), and a context of its own in place of the machine's. |
| `differential` | `true` to write the IR's `differential` block from the scenarios (below). |
| `rules`, `reductions` | Each custom rule and reduction the machine names, by its name in the IR: its C# `type` (an `ICustomRule` or `ICustomReduction` class) and the `example` an in-memory run answers with, whether the rule holds or the fields the reduction sets. |
| `migrations` | The C# `type` (an `IMachineMigration` class) of each step to the machine's `version`, by the version it steps from: one for every version below it, and none other. |
| `triggers` | Each trigger by name: its `phrase`, with `{subject}` where the instance is named and `{field}` for each of its `input` fields, and a `reduce` that sets context from the input. A phrase that starts with `{subject}` is something that happens to the instance, fired by no person. A `guard` names the guard that decides it when more than one tells its moves from its refusals, and an `effect` makes it a send (below). |
| `guards` | Each guard by name: its `holds` and `fails` phrases and its `rule`, which the features set up with Givens. |
| `invokes` | Each state that runs a train: the `train`'s interface full name, its `input` from context fields (or from `$owner`, below), its `output` fields, its `outcomes` by phrase (`done`, `failed` or `cancelled`, a `when` rule on the output, a `reduce`), and `examples`, the output an in-memory run answers with. |
| `links` | Each phrase that names another machine's instance through a context field holding its id. |

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
| `Given Ada is a customer` | Ada is a person of that actor. |
| `Given Ada has an order` | Ada starts an instance of her own. |
| `Given Ada's order is placed`, `Given the shop is open`, `Given ticket "T-12" is new` | The instance is in that state. Arranged, not acted: it fires nothing. A keyed system machine's instance is named by its name and each part of its key, quoted. |
| `Given Ada's order has something in it` | The guard holds (or, with its `fails` phrase, fails): its context is set so. |
| `When Ada places her order` | Ada fires the trigger on her instance, which grants her actor the trigger. |
| `When Ada's order starts baking`, `When the shop opens` | The server fires it: no person is granted it. |
| `When the payment goes through` | The train the state invokes ends with that outcome. |
| `When 5 seconds pass` | Time passes (`seconds`, `minutes`, `hours`, `days`); a timer that falls due fires. |
| `Then Ada's order is placed`, `Then Ada's order is still a draft` | Where the instance is. A When's Thens that name other instances moving are the policies the move sets off. |
| `Then Ada is told "Add something first."` | The When was refused by a guard, with that message. |
| `Then Ada is not allowed` | Ada's actor may not fire the When's trigger: it is not served to her. |
| `Then Ada's orders show status "Placed"` | What one field of a view shows Ada: her own instances and the system's. |

Derivation refuses a data table, a DocString, a `*` step, a first-person step, a step two phrases match, a Given or
When no phrase matches, and two scenarios that disagree. A Then no phrase matches is about the UI: it is reported and
left alone, and `trax machine run --strict` fails on it.

## How a scenario is read

Derivation walks each scenario step by step and reads its shape as well as its words. These are the rules a scenario
has to follow; `trax machine explain` prints what derivation read from each one.

| Rule | Write | Otherwise |
| --- | --- | --- |
| A When is followed by a Then about the instance it moved, before the next When. | `When Ada places her order` / `Then Ada's order is being paid for` / `When the payment goes through` | "Nothing after this When says what it did." |
| An outcome's first Then names the state it took the instance to, and an outcome goes to one state in every scenario. | `When the payment goes through` / `Then Ada's order is placed` | "Say where 'the payment goes through' takes Ada's order", or "goes to Baking here and to Placed elsewhere". |
| The Thens after a When follow the cascade in order. The first about the acting instance says where the When took it; one naming it in another state, after another instance moved, says where a policy took it on. | `When Ada places her order` / `Then Ada's order is placed` / `And the shop is baking a batch` / `And Ada's order is baking` | Naming it in two states with no other instance moving between: "The Thens after this When disagree about what it did to Ada's order." |
| A later Then about another instance moving is a policy the move set off. A user's machine moving the system's is `toSystem`, the system's moving every user instance in a state is `broadcast`. | `When Kit fires the oven on his shift` / `Then Kit's shift is at the oven` / `And the shop is baking a batch` / `And Ada's order is baking` | Nothing: it is derived as a policy. Check it with `trax machine explain`. |
| A trigger a policy fires also has a scenario of its own, showing it as a plain move. | `Given the shop is closed` / `When the shop opens` / `Then the shop is open` | "shop goes from Closed to Open when shift enters On, and no scenario shows a trigger doing that." |
| Two timers falling due at the same moment read as one moving the other. Give them different durations, or show them in different scenarios. | `When 2 seconds pass` / `Then Kit's shift is on` / `And the shop is still baking a batch` | "shop goes from Firing to Open when shift enters On". |
| `still` after time passing says the timer has not fallen due; after a trigger it says the trigger was refused. | `When 3 seconds pass` / `Then the shop is still open` | Two different facts from one word: read it as the scenario's When decides. |
| A Then naming the state the trigger left from is a self-transition: it is served, changes nothing and sets no policy off. Use it where a policy may land on a machine already there. | `Given the shop is open` / `When the shop opens` / `Then the shop is open` | Without it the policy's fire is refused and logged each time. |
| A trigger only some roles may fire does not lead back to the state an instance starts in: whoever may start one could put it there anyway. | Lead it to a state of its own (`On --ClockOut--> Over`). | "the rule would gate nothing, since whoever may start one can put it there." |
| Timers alone do not form a loop. Let an event lead out of one of the states. | `Open --Batch--> Firing`, `Firing --after 4 s--> Open` | "the timers of Firing -> Open -> Firing form a cycle no event breaks", at each scenario that showed one of them; the host refuses the same loop. |
| A person fires triggers on their own instances only, and never on a system machine. | `When the shop opens` (the server), not `When Kit opens the shop`. | "only the server moves a system machine", or "acts on Ada's order". |

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
its policies. A loop that passes through a timer is paced by it and allowed, so a shop that opens and closes on a timer
can still have policies on both.

## access.txt

One sorted line per grant, committed and reviewed: each actor's roles (`@customer: roles customer`), then each
machine's `start`, `read` and trigger, and each view, with the actors its rule admits. A machine several actors own
grants each trigger to the owning actors the scenarios show firing it (`assignment Triage: triage`,
`assignment SubmitFix: fixer`), `start` to those shown starting one, and `read` to all of them; each still acts on their
own instances only. A rule whose roles would also admit an owning actor a scenario shows refused (`Fay is not
allowed`) is a derivation problem, never a wider grant. An actor a rule admits by its
roles that no scenario shows is marked `(no scenario)`. `from-gherkin --check` fails when it is stale, so a new grant
is a line in the pull request.
