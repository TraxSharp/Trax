# What to edit, and where it shows

Each row is a change a user asks for, what to write in the lexicon and the features, and what `from-gherkin` then
changes (besides `machines/glossary.json`, which moves with every lexicon change). Check the last column in
the diff after deriving: if something else moved, a scenario says more than you meant.

## Changes

| To | Write | Then changes |
| --- | --- | --- |
| Add a state | `State: <Name> "<phrase>"` under the machine, and a scenario that reaches it | the IR's `states`, the client's state enum |
| Let an actor do something new | `Trigger: <Name> "<phrase>"`, and a scenario where a person of that actor fires it on their own instance | IR transition and `triggers`, `host.json` `triggers.<T>.roles`, a new `access.txt` line, a new mutation |
| Make something happen with no person | a phrase starting with `{subject}` (or a system machine's phrase), fired by no one in its scenario | IR transition and `triggers`; `host.json` `triggers.<T>.server: true`; `access.txt` `the server only`; no mutation |
| Take input with a trigger | `Input: <field> is a <type>` under the trigger, `{field}` in the phrase, `Sets: <field>`, and a quoted value in the step | IR `inputs.<T>`, the mutation's `input` |
| Refuse a move with a message | `Guard: <name> when <rule>` with `Holds:` and `Fails:`, a scenario where it holds and one where it fails with `Then Ada is told "..."` | the IR transition's `guard` and `guardMessage` |
| Forbid an actor | a scenario with `Then Ada is not allowed`, and no scenario where that actor fires it | the trigger left out for that actor; if no actor fires it, left out entirely and `access.txt` says `nobody` |
| Wait, then move | `When <n> <unit> pass` then a Then in the new state | IR `timers` (`<State>.after`) |
| Run work in a state | `Runs: <train> in <State>` with `Takes:`/`Input:`, `Returns:` (both optional with `--trains`) and an `Outcome:` per phrase, and scenarios for done, failed, cancelled | IR `outcomes`; `host.json` `invokes`; the train is found by its interface's full name in the assemblies given to `ServeFeatures` |
| Give the train its caller | `Input: <field> from the owner` under `Runs:` | `host.json` only; the server sets it |
| React in another machine | a Then about the other instance after the When, and a scenario showing that trigger as a plain move | the target's transition, server-only; `host.json` `policies` |
| React in another machine of the same owner | as above, plus `Link: "<phrase>" to <source> by <field>` on the target | the policy is `linked`, through the link's field |
| Shared state everyone reads | `Machine: <id> "<name>"` with `System` or `System at: <key>`, moved only by policies and timers, and a view over it | `host.json` `owner: { "system": ... }`; the host starts its instance |
| One system instance per key | `System keyed by: <field>, ...`; host code starts and moves each one | no view, policy or person may touch it |
| Let a reader see fields | `View: "<name>"` with `Reads:`, `Field:` and `Label:` lines, and a scenario `Then Ada's <view> show(s) <field> "<value>"` | `host.json` `views` with its readers; `views { ... }` in GraphQL; `views.g.ts` |
| Run a once-only side effect | `Effect: <ISnapshotEffect interface>` under the trigger; its owner sends it with no input | IR trigger with `keyPrefix`; `host.json` `effects` |
| Use a rule or reduction the vocabulary lacks | `Rule:` or `Reduction:` with a C# type and an `example`; tag scenarios needing the real handler `@host` | IR `custom` by name; `host.json` `handlers` |
| Change a machine with stored instances | bump the machine's `Version:` and add a `Migrates from: <n> with <IMachineMigration>` per older version | IR `version`; `host.json` `migrations` |

## A lexicon line, by where it goes

| Lexicon | IR | host.json | C# builder |
| --- | --- | --- | --- |
| `Owned by:` | | `owner: "user"`, `start`, `read` | user-owned (the default) with `Start(s => s.Requires(...))`, `Get`, `Watch`, `Discard` |
| `System`, `System at:`, `System keyed by:` | | `owner: { "system": ... }` | `SystemOwned()` |
| a machine's `Version:`, `Migrates from:` | `version` | `migrations` | `Version(n)`, `MigrateFrom(n, ...)` |
| `Begins in:` (or the first `State:`), `Starts with:` | `initialState`, `initialContext` | | `StartsAt(state, () => ...)` |
| `Context:` and the fields lines write, a state's own `Context:` | `context` | | `In(state).Context<T>()` |
| `committed` on a `State:` line, a state's `Requires:` | `committedStates`, `invariants` | | `Committed()`, `Requires(rule)` |
| `Trigger:` with `Input:` and `Sets:` | `triggers`, `inputs`, transitions | `triggers` rules | `On(trigger).WithInput<T>().Reduce(...).To(state)`, `.Requires(role)` |
| `Guard:` | a transition's `guard`, `guardMessage` | | `.When(rule).Because("...")` |
| `Runs:` and its `Outcome:` lines | `outcomes` | `invokes` | `In(state).Invokes<TTrain, TIn, TOut>(...)`, `OnDone`, `OnFailed`, `OnCancelled` |
| (time passing) | `timers` | | `In(state).After(delay).GoTo(target)` |
| `Rule:`, `Reduction:` | `custom` | `handlers` | `CustomGuard`, `CustomReducer` |
| `Effect:` | the trigger's key prefix | `effects` | `.RunsOnce<TEffect>(keyPrefix)` |
| `Link:` | | a `linked` policy's `link` | none |
| `View:` | | `views` | none |
| (a Then moving another instance) | | `policies` | none |

## How to read access.txt

```
@passenger: roles passenger         an actor and the roles that make a caller one
booking start: passenger            who may start one of their own
booking read: passenger             who may read their own
booking CheckIn: passenger          who may fire CheckIn on their own
booking Miss: the server only       no client may fire it
booking Board: nobody               every scenario denies it, so it is not served
view departures: agent, passenger   who may query the view
```

An actor marked `(no scenario)` is admitted by a rule's roles without any scenario showing it. That is a role design
to fix, not a grant someone asked for.
