---
title: Machine
description: "Reference for Machine<TState, TTrigger> and its builders: IMachineBuilder, IStateBuilder, IInvokeBuilder, ITransitionBuilder, rules and IR export."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 2
package: Trax.Effect.StateMachine.Persistence
namespace: Trax.Effect.StateMachine.Persistence
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/Machine.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Fluent.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Invokes.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Timers.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/MachineTrigger.cs
redirect_from:
  - /docs/sdk-reference/statemachine-api/fluent-authoring
---

# Machine

A machine is a subclass of `Machine<TState, TTrigger>` that declares itself in `Configure`. `TState` and `TTrigger`
are the host's own enums. The builder compiles to the same total engine (`SnapshotMachine<TState, TTrigger>`) the
conformance fixtures drive. `Machine<TState, TTrigger>` ships in `Trax.Effect.StateMachine.Persistence`; the builder
interfaces ship in `Trax.Effect.StateMachine`.

## Signature

```csharp fragment
namespace Trax.Effect.StateMachine.Persistence;

public abstract class Machine<TState, TTrigger> : IMachine
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    protected abstract void Configure(IMachineBuilder<TState, TTrigger> machine);
}
```

## Members

`IMachine` is the machine-agnostic handle a host discovers and the registry keys on. `Machine<TState, TTrigger>`
implements it.

| Member | Type | Description |
|--------|------|-------------|
| `Name` | `string` | The machine's id, from `Id(...)`. |
| `HasEffect` | `bool` | Whether the machine binds an effect with `RunsOnce`. |
| `SchemaHash` | `string?` | Lowercase-hex SHA-256 of `ExportIr()`; null for a machine whose IR export is refused. See [Runtime integrity](/docs/sdk-reference/statemachine-api/runtime-integrity). |
| `Triggers` | `IReadOnlyList<MachineTrigger>` | Every trigger, with its `TriggerKind` and input record. See [Triggers and states](#triggers-and-states). |
| `States` | `IReadOnlyList<MachineState>` | Every state, with its context record. |
| `StartInputType` | `Type?` | The record `Start<TStartInput>` declares, or null. |
| `Corpus` | `string?` | The machine's committed differential corpus, or null. Virtual: a machine ships one by overriding it, for example from an embedded resource. |
| `SelfCheck()` | `IReadOnlyList<string>` | Replays `Corpus` through the machine's engine; one diff per case it does not reproduce. |
| `ExportIr()` | `string` | The machine's [IR](/docs/sdk-reference/statemachine-api/ir-format) as canonical single-line JSON. See [Exporting the IR](#exporting-the-ir). |
| `CreateService(ISnapshotStore store, IEffectClaimStore? claims, TimeSpan? draftTtl = null)` | `ISnapshotDraftService` | Builds a draft service over a store. A service built here cannot start an instance and applies no per-user cap; the one `ISnapshotMachineRegistry.Service` returns can. `draftTtl` is not read: the draft sweep expires drafts. |
| `CreateEffectRunner(ISnapshotDraftService service, IdempotentEffect idempotent, IServiceProvider services)` | `ISnapshotEffectRunner?` | Builds the effect runner, resolving the effect from `services`; null for a machine with no effect. Hidden from IntelliSense; the registry calls it. |

`Triggers`, `States` and `StartInputType` have default implementations on `IMachine` that throw
`NotSupportedException`; derive from `Machine<TState, TTrigger>` to get them.

## IMachineBuilder

| Method | Description |
|--------|-------------|
| `Id(string id)` | The machine's stable name (what the `machine` mutation field selects). It must be kebab-case, matching `^[a-z][a-z0-9]*(-[a-z0-9]+)*\z` (`checkout`, `turnstile-two`; a trailing newline is refused too), because it becomes a file name, a module name and a key segment in everything generated from the machine. Anything else throws `ArgumentException`. |
| `Version(int version)` | The definition version stamped onto every snapshot. |
| `StartsAt(TState state, Func<JsonObject> context)` | The initial state and a factory for its context. A factory, not a value, so instances never alias. |
| `MigrateFrom(int fromVersion, Func<string, JsonObject, MigrationResult> migrate)` | Forward-migrate a stored snapshot from `fromVersion` to this definition's version. The migrator gets the stored state name and context and returns a `MigrationResult`. |
| `Differential(Action<IDifferentialBuilder> configure)` | Authors the cross-language differential fuzzing inputs (test-only): per-trigger input samples, per-state seed contexts, and dense probe contexts. Exported into the IR's `differential` block so the differential harness enumerates off the one C# source, with no hand-written machine.json. Only valid on a declaratively-authored machine. See [IDifferentialBuilder](#idifferentialbuilder). |
| `In(TState state)` | Opens a state to declare its context rule and outgoing transitions. Returns an `IStateBuilder`. |
| `SystemOwned()` | The machine's instances belong to the system: only [`IMachineInstances.Start`](/docs/sdk-reference/statemachine-api/machine-instances) creates one, and no user's draft operation reaches the machine (they answer `unknown-machine`). A train it invokes is authorized in the trusted execution scope, so the host refuses one that declares `[TraxAuthorize]`. Without it a machine is user-owned and `Start` refuses it. See [Invoking a train](/docs/statemachine/invoking-trains#who-a-run-belongs-to). |
| `InvokedRunLimit(int limit)` | The most live invoked runs one user may already hold, counted across every machine, for entering one of this machine's invoking states to be allowed; past it the entry is refused with `invoke-limit-reached`. Defaults to 10; at least 1. System owners are not capped. See [Invoking a train](/docs/statemachine/invoking-trains#who-a-run-belongs-to). |
| `CustomGuard(string name, Func<JsonObject, JsonNode?, bool> guard)` | Binds the C# handler for `Rule.Custom(name)`, wherever the machine uses it (a `When` or a `Requires`, nested or not). The TypeScript twin's `customGuards` is the other half. |
| `CustomReducer(string name, Func<JsonObject, JsonNode?, JsonObject> reducer)` | Binds the C# handler for `Reduction.Custom(name)`: it gets the context and the trigger input and returns the destination context. The twin's `customReducers` is the other half. |

`Build()` throws `InvalidOperationException` for a custom rule or reduction whose name has no handler bound,
so an unbound name fails when the machine is built rather than refusing its edge forever. It also throws for a
machine that binds more than one effect with `RunsOnce`: a machine runs exactly one irreversible effect, and a
second binding would otherwise be declared and never run. And it throws for a machine that enters its effect's
target state by any transition other than the effect's own: that state means the effect ran, and a plain advance
of the other edge would put a draft there without it. A self-loop on the target does not enter it and is
allowed. Handlers may be
bound before or after the rules that name them. `CustomGuard` and `CustomReducer` are default interface
methods that throw `NotSupportedException` on a custom `IMachineBuilder` implementation that does not
override them.

## IStateBuilder

| Method | Description |
|--------|-------------|
| `Holds(Func<JsonObject, string?> validator)` | The state's context rule: return `null` when valid, or a reason string. Enforced on rehydrate and on advance. |
| `Context<TContext>()` | The declarative, string-free replacement for `Holds`: the state's context shape comes from a record. Field names, JSON types, nullability, and attribute constraints (`[MinLength(1)]`) become both the validator and the exportable schema. |
| `Context()` | Declares that the state carries no context (an empty schema). |
| `Requires(Rule constraint)` | A per-state policy layered on the schema (composed, ANDed, chainable): what the state demands beyond its shape, e.g. a complete draft or an absent receipt. Exports as the state's `invariants` entry. Build the rule with the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). |
| `Committed()` | Marks the state as completed. A soft autosave can neither put a draft into it nor move a draft out of it (except a reset to the initial state); only the effect runner puts a draft there. See [what each path may write](/docs/sdk-reference/statemachine-api/persistence-ports#what-each-path-may-write). |
| `On(TTrigger trigger)` | Starts a transition out of this state. Returns an `ITransitionBuilder`. |
| `Invokes<TTrain, TInput, TOutput>(Func<JsonObject, TInput> input)` | Runs the train `TTrain` (its interface, an `IServiceTrain<TInput, TOutput>`) whenever the machine enters this state; `input` builds the run's input from the context, on the server only. Returns an [`IInvokeBuilder`](#iinvokebuilder). A state invokes at most one train. See [Invoking a train](/docs/statemachine/invoking-trains). |

## IInvokeBuilder

Says where an invoked train's outcome goes. It is also the state's
`IStateBuilder`, so the state's transitions follow it. Every target joins the machine's reserved states.

| Method | Description |
|--------|-------------|
| `OnDone(TState target, Rule? when = null, Reduction? reduce = null)` | Where a successful run goes. Declare one or more; they are tried in declaration order and the first whose `when` holds for the output is taken (null: every output). `when` reads the output as the outcome's input (`Input((MyOutput o) => o.Field)`); `reduce` builds the target's context from it (null keeps the context). An output no edge accepts is a `no-transition` to the engine; on the server the finished run is applied as `OnFailed` with the reason `invoke-output-unaccepted`. |
| `OnFailed(TState target, Reduction? reduce = null)` | Where a failed run goes, a reaped run included. Exactly once. The outcome carries no input. |
| `OnCancelled(TState target, Reduction? reduce = null)` | Where a cancelled run goes. Required, exactly once. The outcome carries no input. |

`Build()` throws `InvalidOperationException`, naming the state, when an invoking state lacks `OnDone`, `OnFailed`
or `OnCancelled`, declares either of the last two twice, invokes a second train, names the train by a class
rather than its interface, or names a tuple as the output (its elements are fields, which no guard or reduction can
read). It also throws when an outcome goes to the target of the `RunsOnce` effect, or to the state the machine
`StartsAt` (which would be reserved, so no draft could ever be created), or when an ordinary transition enters a
state an outcome goes to (a self-loop is allowed). An invoked train does not count
against the one `RunsOnce` effect.

## ITransitionBuilder

| Method | Description |
|--------|-------------|
| `When(Func<JsonObject, JsonNode?, bool> guard)` | Admits the transition only when the guard passes. The first matching edge wins. |
| `When(Rule guard)` | The declarative, exportable guard: admits the edge only when the `Rule` holds. Build rules with the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). |
| `WithInput<TInput>()` | Declares this trigger's input as a record. Both engines check every input sent with the trigger against the record's schema before any guard reads it, as strictly as the CLR type, and refuse a mismatch as `invalid-input` (see [Trigger input](#trigger-input)). The IR carries the schema. |
| `Because(string message)` | The detail surfaced when the guard rejects the trigger. |
| `Reduce(Func<JsonObject, JsonNode?, JsonObject> reducer)` | Computes the next context. Return a fresh JSON object; never mutate the input. |
| `Reduce(Reduction reduce)` | The declarative, exportable reducer: `Set(...).FromInput(...)`, `Clear()`, `Reset()`, `Keep()`. See the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). |
| `RunsOnce<TEffect>(string? keyPrefix = null)` | Binds an `ISnapshotEffect` that runs exactly once when this transition is sent, keyed on `{keyPrefix}:{user}:{id}`. Omit `keyPrefix` and it defaults to `{machineId}:{trigger}`. Only the send fires this transition: an advance of its trigger is refused as `effect-bound`. A machine binds at most one. |
| `To(TState state)` | The destination state. Also closes the transition, so you can chain another `On(...)`. |

## IDifferentialBuilder

Authors a machine's differential fuzzing inputs, consumed only by the cross-language differential test: the
harness enumerates the reachable space plus these inputs, records each outcome into a committed corpus, and
every runtime replays it. Declared in C# so the machine is the single source; the IR carries them and the
generated runtime machine strips them. The harness always fires a no-input case per trigger, so an explicit
empty sample is a distinct case. Typed overloads serialize the record with camelCase names (nulls kept);
raw `JsonObject` overloads give exact control.

| Method | Description |
|--------|-------------|
| `Sample<TInput>(TTrigger trigger, TInput input)` | A representative input for a trigger, from a typed record. |
| `Sample(TTrigger trigger, JsonObject input)` | The same, as a raw JSON object. |
| `EmptySample(TTrigger trigger)` | An empty (`{}`) input for a trigger, distinct from the always-added no-input case. |
| `Seed<TContext>(TState state, TContext context)` / `Seed(TState state, JsonObject context)` | A seed context used as a BFS start point, reaching states the initial snapshot can't. |
| `Probe<TContext>(TContext context)` / `Probe(JsonObject context)` | A dense probe context crossed with every state, exercising guards and validators on unreachable-but-sendable snapshots. |
| `OutcomeSample<TOutput>(TState invokingState, TOutput output)` | A representative output of the train the state invokes, fired as its `<State>.done` outcome trigger. Every outcome trigger is also fired with no input. `Build` refuses a sample for a state that invokes nothing. |

## Rules, start, reads and timers

These declarations describe how a machine is served to clients. Each event, the machine's `start` and its reads
declare who may reach them, and the server checks every event and every `start` against its rule (see
[persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#actors-start-and-discard)). The draft
service's `Start` validates the start input against its record and applies the reduction to the initial context, and
a host refuses to start while the start of a machine a client can reach copies its input into a field an invoked
train's outcome, the effect or an event gated by `Requires` or `RequiresPolicy` writes.

| Method | Description |
|--------|-------------|
| `ITransitionBuilder.Requires(params string[] roles)` | Only an authenticated caller holding one of `roles`, compared exactly and case-sensitively, may fire this event. Chained `Requires` calls each add a set the caller must also meet. |
| `ITransitionBuilder.RequiresPolicy(string policy)` | Only an authenticated caller who satisfies the authorization policy may fire this event. It combines with `Requires`. |
| `ITransitionBuilder.AnyAuthenticated()` | Any authenticated caller may fire this event. |
| `ITransitionBuilder.Anonymous()` | Any caller may fire this event. A host refuses it on a user-owned machine a client can reach. |
| `IMachineBuilder.Start<TStartInput>(Action<IStartBuilder<TStartInput>> configure)` | Declares the machine's `start`: the record a client starts an instance with, never the context type. `IStartBuilder` takes a declarative `Reduce(Reduction)` from the start input to the initial context, and the same four rules as an event (`Requires`, `RequiresPolicy`, `AnyAuthenticated`, `Anonymous`). |
| `IMachineBuilder.Get(Action<IReadRuleBuilder> rule)` | Declares who may read an instance through `get`. |
| `IMachineBuilder.Watch(Action<IReadRuleBuilder> rule)` | Declares who may watch an instance through `machineChanged`. `IReadRuleBuilder` offers `OwnerOnly()`, `Requires(...)`, `RequiresPolicy(...)`, `AnyAuthenticated()` and `Anonymous()`. |
| `IMachineBuilder.Discard(Action<IReadRuleBuilder> rule)` | Declares who may delete an instance through `discard`, owner-only being the floor as for `get`. Whatever the rule, a row the server put in its state (an effect's record, an invoked train's outcome, an event gated by `Requires` or `RequiresPolicy`, carried on by a timer), an instance in a state that invokes a train, and one with an effect pending on it is not discarded: the answer is `discard-not-allowed`. |
| `IStateBuilder.After(TimeSpan delay)` | Declares the state's timer: once the state has been held for `delay`, the server leaves it. Returns an `ITimerBuilder`, whose `GoTo(TState target)` names where it goes and returns the state builder. The timer starts on every entry (a self-transition included) and is cancelled on leaving; its trigger is `<State>.after`. |
| `IStateBuilder.After<TContext>(Expression<Func<TContext, TimeSpan?>> duration, TimeSpan min, TimeSpan max)` | The same, with the duration read from a context field and bounded by `min` and `max`. The field holds an ISO-8601 duration (`"PT5M"`: days, hours, minutes and seconds, in ASCII digits) and is read once, when the state is entered; entering with a field that is missing, not such a duration, or outside the bounds is refused as `invalid-input`, never an error. The typed GraphQL surface writes and reads a `TimeSpan` as this duration. |

An event has one rule, whichever state it is sent from: `Build` refuses a machine whose edges of one event declare
different rules (or one declares a rule and another none), and one where a gated event (one that names a role or a
policy) enters the start state from another state, since anyone allowed to start an instance can put one there (unless the start
state invokes a train, where the startup comparison below is the gate). `AnyAuthenticated()`
and `Anonymous()` stand alone; combining either with another rule on one edge throws. Outcome and timer triggers
are the server's own and offer no rule. At startup the host compares every event entering an invoking state, and
the start into an invoking initial state, with the invoked train's `[TraxAuthorize]`, and refuses one whose rule
does not prove the train's: an event's role set must sit inside each of the train's role sets, and it must name
each of the train's policies, since a policy cannot be proven to grant a role.

A state has at most one timer. `Build` refuses two timers on one state, a timer back into its own state, timers that
form a cycle no event breaks, a timer into an effect's target or an invoked train's outcome target, a fixed duration
that is not positive or is above `MachineTimers.MaxDuration` (366 days), bounds whose minimum is not positive or is
above the maximum or whose maximum is above the cap, and a duration field the state's declared context does not have,
does not type as `TimeSpan?`, or marks `[TraxSensitive]` (a deadline is exported and shown). A selector that is not a
single field builds, but the IR export refuses the machine. A timer's target is not reserved, because a timer carries
the context unchanged; the timed state itself is locked against the soft save, and the generic save
refuses a machine with a timer. A host refuses to start a user-owned machine whose timer enters a state that invokes a
train, since nobody is present to authorize the run.

## Trigger input

A trigger's input is validated in the engine, so every path that fires a trigger checks it: the draft service,
the generic advance, and a host's `IMachineInstances.Advance`, which is trusted for rules but never for input.
The TypeScript twin validates against the same exported schema and gives the same verdict
([ADR 0048](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md)).

- **A declared record is checked as strictly as its CLR type.** A missing non-nullable field, a field of another
  JSON type, a fraction in an integral field (`2020.0` is an integer, `1.5` is not), a field the record does
  not declare (a PascalCase key is an unknown field, not an alias), and anything other than an object are
  refused. No input reads as `{}`. A nested record, and each element of a list of records, is checked the same
  way. Constraints such as `[MinLength(1)]` are checked against the input, never the context.
- **Names follow System.Text.Json.** A `[JsonPropertyName]` is the field's name, and a `[JsonIgnore]` member is
  not part of the input.
- **A declarative trigger without `WithInput` takes no input**: only none or `{}` is accepted. An effect-bound
  one takes the `receipt` its effect runner hands it. A trigger whose guard or reducer is a delegate reads the
  input in code nothing can describe, so its input is left to that code unless it declares `WithInput`.
- **Some machines refuse to build.** A record that contains itself has no finite schema, so `WithInput<T>()`
  throws naming it. One trigger has one input schema: `Build()` refuses a trigger whose edges declare different
  records, or a record on one edge and none on another. An invoked train's output type needs a schema too,
  since its output is checked against it before an `OnDone` guard reads it.
- **Never an exception.** A duplicate key, a number outside the range of a double (`1e400`), a string with a
  lone surrogate, or a deeply nested object is refused as `invalid-input`.

## Exporting the IR

`ExportIr()` turns a built machine into its canonical [IR](/docs/sdk-reference/statemachine-api/ir-format), the
same JSON the [`trax machine` CLI](/docs/reference/cli#state-machines-trax-machine) writes to `<machine>.ir.json`;
the CLI calls `ExportIr()`. It throws `InvalidOperationException` for a machine that made no declarative call, and
for one that mixes the styles and still has a delegate guard (`When(Func...)`), delegate reducer (`Reduce(Func...)`)
or `Holds` validator; the message names each edge (source state, trigger, target) and state.

```csharp compile
string ir = new CheckoutMachine().ExportIr();
```

## Behavior

### Triggers and states

`IMachine.Triggers` lists a machine's triggers as `MachineTrigger(Name, Kind, InputType)` records, where `Kind` is
a `TriggerKind`: `Client`, `EffectBound`, `Outcome` or `Timer`. `Machine<TState, TTrigger>` lists every member of
its trigger enum (`EffectBound` when an edge on it binds the machine's effect, else `Client`, with the record its
`WithInput<T>()` declares or null), then each invoking state's `<State>.done`, `.failed` and `.cancelled` as
`Outcome`, the success outcome's input type being the train's output type, then each timed state's `<State>.after`
as `Timer`, with no input. A typed API is built from this list, and offers no operation for an outcome or a timer,
with two more: `IMachine.States` lists every member of the state enum as a `MachineState(Name, ContextType)`, where
`ContextType` is the record the state's `Context<TContext>()` declares (null for `Context()` or none), and
`IMachine.StartInputType` is the record `Start<TStartInput>` declares, or null when the machine declares no start.
The [machines root](/docs/sdk-reference/graphql-api/machines) types each exposed machine's state, context and
operations from the three.

The engine (`SnapshotMachine`) fires a timer trigger through an entry point of its own, which only the server's reconciler calls, kept
apart from `Advance`, which refuses one as `timer-bound`. It fires only the timer of the state the snapshot is in (any other
is `no-transition`) and carries the context to the timer's target. The IR exports timers in a `timers` block of their
own (state, trigger, target, `after` or `field` with `min` and `max` as ISO-8601 durations, and the union of the rules
of everything that enters the state), never in `inputs` or `triggers`; a machine without timers exports no block.

### Delegate vs declarative

`Holds`, `When(Func...)` and `Reduce(Func...)` take delegates; `Context<T>`, `When(Rule)`, `Reduce(Reduction)` and
`WithInput<T>` take data and also record the `DeclarativeModel` the IR is exported from. Both compile to the same
engine delegates. See [Delegates and declarative rules](/docs/statemachine/delegates-and-declarative-rules).

### Results

Every advance and rehydrate returns a typed result, never an exception: see
[Result codes](/docs/sdk-reference/statemachine-api/result-codes).

## Example

The turnstile from the State Machine sample:

```csharp from=Trax.Samples/samples/StateMachine/Trax.Samples.StateMachine/Machines.cs
[TraxMachine]
public sealed class TurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id("turnstile")
            .Version(1)
            .StartsAt(TurnstileState.Locked, () => new JsonObject())
            .Start<EmptyStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .AnyAuthenticated()
            .WithInput<CoinInput>()
            .When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Context<UnlockedContext>()
            .On(TurnstileTrigger.Push)
            .AnyAuthenticated()
            .Reduce(Clear())
            .To(TurnstileState.Locked);
    }
}
```

## See also

- [Authoring a machine](/docs/statemachine/authoring) and [Declarative authoring](/docs/statemachine/declarative-authoring).
- [Delegates and declarative rules](/docs/statemachine/delegates-and-declarative-rules).
- [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules) and the [declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model).
- [Invoking a train](/docs/statemachine/invoking-trains).
- [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines).
