# What to edit, and where it shows

Each row is a change a user asks for, what to write, and what `from-gherkin` then changes. Check the last column in
the diff after deriving: if something else moved, a scenario says more than you meant.

## Changes

| To | Write | Then changes |
| --- | --- | --- |
| Add a state | `states.<Name>: "<phrase>"` in the glossary, and a scenario that reaches it | the IR's `states`, the client's state enum |
| Let an actor do something new | a trigger with a `phrase`, and a scenario where a person of that actor fires it on their own instance | IR transition and `triggers`, `host.json` `triggers.<T>.roles`, a new `access.txt` line, a new mutation |
| Make something happen with no person | a phrase starting with `{subject}` (or a system machine's phrase), fired by no one in its scenario | IR transition only; `host.json` `server: true`; `access.txt` `the server only`; no mutation |
| Take input with a trigger | `input` fields, `{field}` in the phrase, a `reduce`, and a quoted value in the step | IR `inputs.<T>`, the mutation's `input` |
| Refuse a move with a message | a guard (`holds`, `fails`, `rule`), a scenario where it holds and one where it fails with `Then Ada is told "..."` | the IR transition's `guard` and `guardMessage` |
| Forbid an actor | a scenario with `Then Ada is not allowed`, and no scenario where that actor fires it | the trigger left out for that actor; if no actor fires it, left out entirely |
| Wait, then move | `When <n> <unit> pass` then a Then in the new state | IR `timers` (`<State>.after`) |
| Run work in a state | `invokes.<State>` (train interface, input, output, outcomes, examples) and scenarios for done, failed, cancelled | IR `outcomes`; `host.json` `invokes`; the train is found by its interface's full name in the assemblies given to `ServeFeatures` |
| Give the train its caller | `"input": { "<field>": "$owner" }` | `host.json` only; the server sets it |
| React in another machine | a Then about the other instance after the When, and a scenario showing that trigger as a plain move | the target's transition, server-only; `host.json` `policies` |
| React in another machine of the same owner | as above, plus a `links` entry on the target | the policy is `linked`, through the link's field |
| Shared state everyone reads | a machine with `system` and `name`, moved only by policies and timers, and a view over it | `host.json` `owner: { "system": ... }`; the host starts its instance |
| One system instance per key | `system: { "keyedBy": ["id"] }`; host code starts and moves each one | no view, policy or person may touch it |
| Let a reader see fields | a view (`reads`, `fields`, `labels`) and a scenario `Then Ada's <view> show(s) <field> "<value>"` | `host.json` `views` with its readers; `views { ... }` in GraphQL; `views.g.ts` |
| Run a once-only side effect | an `effect` on the trigger (an `ISnapshotEffect` interface); its owner sends it with no input | IR trigger with `keyPrefix`; `host.json` `effects` |
| Use a rule or reduction the vocabulary lacks | `rules` or `reductions` with a C# type and an `example`; tag scenarios needing the real handler `@host` | IR `custom` by name; `host.json` `handlers` |
| Change a machine with stored instances | bump `version` and add `migrations` (an `IMachineMigration` per step from each older version) | IR `version`; `host.json` `migrations` |

## A glossary key, by where it goes

| Glossary | IR | host.json | C# builder |
| --- | --- | --- | --- |
| `ownedBy` | | `owner: "user"`, `start`, `read` | user-owned (the default) with `Start(s => s.Requires(...))`, `Get`, `Watch`, `Discard` |
| `system` | | `owner: { "system": ... }` | `SystemOwned()` |
| `version`, `migrations` | `version` | `migrations` | `Version(n)`, `MigrateFrom(n, ...)` |
| `initial`, `initialContext` | `initialState`, `initialContext` | | `StartsAt(state, () => ...)` |
| `context`, a state's `context` | `context` | | `In(state).Context<T>()` |
| a state's `committed`, `requires` | `committedStates`, `invariants` | | `Committed()`, `Requires(rule)` |
| `triggers` | `triggers`, `inputs`, transitions | `triggers` rules | `On(trigger).WithInput<T>().Reduce(...).To(state)`, `.Requires(role)` |
| `guards` | a transition's `guard`, `guardMessage` | | `.When(rule).Because("...")` |
| `invokes` | `outcomes` | `invokes` | `In(state).Invokes<TTrain, TIn, TOut>(...)`, `OnDone`, `OnFailed`, `OnCancelled` |
| (time passing) | `timers` | | `In(state).After(delay).GoTo(target)` |
| `rules`, `reductions` | `custom` | `handlers` | `CustomGuard`, `CustomReducer` |
| an `effect` | the trigger's key prefix | `effects` | `.RunsOnce<TEffect>(keyPrefix)` |
| `links` | | a `linked` policy's `link` | none |
| `views` | | `views` | none |
| (a Then moving another instance) | | `policies` | none |

## How to read access.txt

```
@passenger: roles passenger         an actor and the roles that make a caller one
booking start: passenger            who may start one of their own
booking read: passenger             who may read their own
booking CheckIn: passenger          who may fire CheckIn on their own
booking Miss: the server only       no client may fire it
view departures: agent, passenger   who may query the view
```

An actor marked `(no scenario)` is admitted by a rule's roles without any scenario showing it. That is a role design
to fix, not a grant someone asked for.
