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
| `initial` | The state an instance starts in. |
| `context` | The fields of its context, as the IR declares fields. |
| `states` | Each state by name, with its phrase (`is a draft`). |
| `triggers` | Each trigger by name: its `phrase`, with `{subject}` where the instance is named and `{field}` for each of its `input` fields, and a `reduce` that sets context from the input. A phrase that starts with `{subject}` is something that happens to the instance, fired by no person. |
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

## host.json

Written by `from-gherkin`, never by hand. Everything only the server reads:

| Key | What it is |
| --- | --- |
| `machines` | Each machine's `schemaHash` (the IR it was derived with), `owner` (`user`, `{ "system": "<key>" }`, or `{ "system": { "keyedBy": [...] } }` for a keyed system machine), who may `start` and `read` it, each trigger's rule (`roles`, `anyAuthenticated`, or `server`), and the train each invoking state runs, with its input from context fields or `$owner`. A trigger with no entry is not served. |
| `policies` | The server acting when a machine enters a state: `linked` (the same owner's instance through a link), `broadcast` (from a system machine, every instance waiting in a state) or `toSystem` (a user's machine moving the system's at its key). |
| `views` | Each view a scenario reads, its fields and its readers. A view no scenario reads is not served. |
| `provenance` | For each part of each machine, the scenarios that produced it, by file, line and name, never by step text. The console's Board tab shows them. |

A host refuses a directory whose IR the host file was not derived from, or whose names disagree with it. It also
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
