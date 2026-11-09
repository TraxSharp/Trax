---
name: trax-write-machines-as-features
description: Use when a Trax app's state machines are written as Gherkin features and a glossary (a features/ folder with glossary.json, a machines/ folder with *.ir.json, host.json and access.txt, a host calling ServeFeatures), or when the user wants behaviour written as scenarios rather than a Machine class. Covers writing and changing the glossary and the .feature files, the step dialect derivation reads (named people, owner's instances, server steps, outcomes, time passing, view reads), trax machine from-gherkin, run, missing, usage, explain and diagram, reviewing access.txt, fixing derivation problems, the Reqnroll host bindings, and how each glossary entry maps to the IR, host.json, GraphQL and the C# builder.
metadata:
  internal: true
---

# Write Trax machines as features

In an app that does this, the behaviour of its machines lives in `features/*.feature` and `features/glossary.json`
and nowhere else. `trax machine from-gherkin` derives `machines/`: one `<machine>.ir.json` per machine,
`host.json` (access, trains, policies, views, provenance) and `access.txt` (one line per grant). The host serves
`machines/` with `ServeFeatures`. There is no machine class and no hand-written rule.

[Write machines as features](https://traxsharp.net/docs/statemachine/write-machines-as-features) is the how-to,
[Feature files](https://traxsharp.net/docs/reference/feature-files) has every key and step, and
[How features become machines](https://traxsharp.net/docs/statemachine/how-features-become-machines) traces each
part to the IR, `host.json`, GraphQL and the C# builder. [mapping.md](mapping.md) is that trace as a lookup: what to
edit for a given change.

## First: is this app written this way

Look for `features/glossary.json`, a `machines/host.json`, and `ServeFeatures("machines", ...)` in the host's
`AddStateMachines`. With all three, every change to a machine's behaviour is a change to the features or the glossary.
Without them the app's machines are C# classes: use `trax-design-a-machine`.

A new machine can go either way. Write it as features unless it needs something only C# has: a child machine
(`Spawns`), a delegate guard or reducer, `RequiresPolicy`, `Anonymous`, `InvokedRunLimit`, a timer whose duration a
context field holds, or one person acting on another person's instance. Say which, and why, before choosing C#.

## Never edit machines/ by hand

`machines/` is output. `host.json` carries each IR's hash and the host refuses a pair that does not match, and
`from-gherkin --check` in CI fails on any file that differs from what the features derive. Change the features or the
glossary, then run `from-gherkin`. The same goes for the client: `trax client` regenerates it from the host's
exported manifest (`trax-generate-a-client`).

## The loop

1. Change the glossary first when a new word is needed: a state, a trigger, a guard, an outcome, a view, an actor.
2. Write or change the scenarios.
3. Derive, which also replays every scenario on what it derived and writes nothing if one fails:
   `trax machine from-gherkin --features features --out machines`
4. Read the problems, if any. Each names a file and line.
   [Problems derivation reports](https://traxsharp.net/docs/reference/feature-files#problems-derivation-reports) says
   what to change for the common ones. `trax machine explain --features features --scenario "<name>"` prints what
   derivation read from one scenario: its edges, guards, timers and policies, and which Then caused which.
5. Read the diff of `machines/access.txt`. Every added line is a new grant. Tell the user about each one in your
   summary, in words (`customers may now cancel their own orders`). A grant you did not mean is a scenario that
   shows the wrong person acting.
6. Run the features on the committed files: `trax machine run --features features --machines machines`.
7. `trax machine missing --features features` lists what the glossary names and no scenario shows, each with a
   stub that is checked to derive clean. Use it before calling the work finished.
8. Run the host tests (below), regenerate the client if the IR changed, and commit `features/` and `machines/`
   together.

## The dialect

Derivation reads a fixed English shape. A step it cannot read is a problem, except a Then, which it treats as a step
about the UI and leaves alone. So a misspelled Then is skipped, not failed: run `trax machine run --strict`, which
fails on every Then it cannot read, and check that each Then you meant to be checked shows up in
`trax machine explain`.

- **Name people, never "I".** `Given Ada is a customer`, then `Ada ...`. `her`, `his` and `their` mean the step's own
  person and nobody else.
- **A person acts on their own instance only.** `When Ada places her order`, never `When Kit places Ada's order`.
  Nobody fires a system machine: `When the shop opens`, not `When Kit opens the shop`.
- **A step with no person is the server's.** `When Ada's order starts baking` makes `Bake` server-only. A trigger is
  either a person's or the server's, never both: use two triggers.
- **Quote every value.** `When Ada orders "3" loaves for her order`. An unquoted value is part of the phrase.
- **Every When is followed by a Then about the instance it moved**, before the next When.
- **An outcome's first Then names the state it reached.** Every invoking state shows `done`, `failed` and
  `cancelled` somewhere.
- **A Then about another instance moving is a policy.** The trigger that policy fires also needs a scenario of its own
  showing it as a plain move (`Given the shop is closed` / `When the shop opens` / `Then the shop is open`).
- **Time passing is a timer:** `When 10 seconds pass` then a Then in a new state. `still` after time passing means
  the timer has not fallen due yet.
- **A refusal is** `Then Ada is told "..."` (a guard) or `Then Ada is not allowed` (her actor may never fire it). A
  trigger that is only ever denied is not served at all.
- **A view is read as** `Then Ada's orders show status "In the oven"`. A view no scenario reads is not served.
- **No data tables, DocStrings or `*` steps.**

[How a scenario is read](https://traxsharp.net/docs/reference/feature-files#how-a-scenario-is-read) has the rest,
each with the message you get when it is broken.

## Rules that are security

These keep access closed. Do not work around them:

- A grant comes only from a scenario showing that actor doing it. Never add a scenario only to make a step pass if it
  also grants something; check `access.txt`.
- Do not give two actors overlapping roles to make one scenario pass. Derivation refuses a rule that would also admit
  an actor a scenario shows refused, and that refusal is correct.
- A system machine is read through a view, never fired by a person. If a person needs to change it, their own machine
  changes state and a policy moves the system machine.
- One person acting on another's instance is refused because Trax has no shared instances yet. Do not route around it
  with a system machine that a person can effectively drive. Tell the user it needs a C# machine or shared instances.

## Glossary syntax that is not obvious

- A field is `{ "name", "type", "nullable", "constraints" }`, as the IR writes one.
- A guard's `rule`, a state's `requires` and an outcome's `when` are IR rules:
  `{ "rule": "compare", "source": "context", "field": "loaves", "op": "gt", "value": 0 }`. A trigger's `reduce` is an
  IR reduction: `{ "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }`.
  [The IR format](https://traxsharp.net/docs/sdk-reference/statemachine-api/ir-format#transitions) lists every kind.
- A trigger phrase has `{subject}` where the instance goes and `{field}` for each input: `orders {loaves} loaves for
  {subject}`. A phrase that starts with `{subject}` happens to the instance, with no person.
- A train's input from `$owner` (`"input": { "memberId": "$owner" }`) gives it the instance owner, set by the server.
  Use it whenever the train checks who it acts for.
- Two user machines of one owner that a policy joins need a
  [link](https://traxsharp.net/docs/reference/feature-files#links): a context field on the target holding the source's
  id.

## Testing

- **In memory:** `trax machine run`, or `FeatureMachines.Run` from a test. It answers as the host does, access
  included, and is fast. Tags, names and `file:line` select scenarios.
- **Against the host:** reference `Trax.StateMachine.Gherkin.Reqnroll` in a Reqnroll test project, include the feature
  files, and call `TraxFeatures.Configure` from a `[BeforeTestRun]` hook with the host in the test process and a
  credential per person per scenario. Set `Manifest` to the host's exported client manifest when the host requires
  persisted operations, so every step goes by its persisted id. A machine feature needs no step code.
  [Feature bindings](https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-bindings) has the options.
- **Other specs** (scheduling, auth, anything that is not a machine) go in `specs/`, which derivation does not read.
  Their own steps are registered with `o.Given/When/Then(pattern, step)` in `TraxFeatures.Configure`, never with
  Reqnroll's `[Given]` attributes, which would be ambiguous with the binding that matches every step.
- Tag a scenario `@host` when it needs a real handler (a custom rule, a reduction or an effect): an in-memory run
  skips it and the host runs it. No other tag means anything to Trax.
- Set `REQNROLL_TELEMETRY_ENABLED=0` where the tests build and run.

## Known mistakes

- Editing `machines/*.json` or `access.txt` by hand. The host or CI refuses it.
- Writing a Then the glossary cannot read and assuming it was checked.
- Copying `machines/` into the build output was forgotten: `ServeFeatures("machines")` reads from the application's
  base directory.
- A state that invokes a train with no scheduler on this host or another sharing the database: the run stays queued
  and the instance waits. `AddScheduler()`, `AddJunctionProgress()` and a relational provider are required, as for
  any invoking machine.
- Policies on `UseInMemory()`: refused, since InMemory has no transaction to queue a policy's fire in.
- A long timer tested against the host: on the host time passes on the wall clock, so keep host scenarios to seconds
  and leave long timers to the in-memory run.

## Next

- The full map of what becomes what: [mapping.md](mapping.md).
- Serve and secure the host: `trax-expose-over-api`. Generate the frontend's client: `trax-generate-a-client`.
- A machine that needs C#: `trax-design-a-machine`.
