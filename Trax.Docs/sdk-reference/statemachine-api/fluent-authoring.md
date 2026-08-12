---
layout: default
title: Machine authoring
parent: State Machine API
grand_parent: SDK Reference
nav_order: 2
---

# Machine authoring

A machine is a subclass of `Machine<TState, TTrigger>` that declares itself in `Configure`. `TState` and
`TTrigger` are your own enums. The fluent builder compiles to the same total engine the conformance
fixtures drive, so a fluently-authored machine behaves identically to a hand-written definition.

```csharp
public abstract class Machine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    protected abstract void Configure(IMachineBuilder<TState, TTrigger> machine);
}
```

## IMachineBuilder

| Method | Description |
|--------|-------------|
| `Id(string id)` | The machine's stable name (what the `machine` mutation field selects). |
| `Version(int version)` | The definition version stamped onto every snapshot. |
| `StartsAt(TState state, Func<JsonObject> context)` | The initial state and a factory for its context. A factory, not a value, so instances never alias. |
| `MigrateFrom(int fromVersion, Func<string, JsonObject, MigrationResult> migrate)` | Forward-migrate a stored snapshot from `fromVersion` to this definition's version. The migrator gets the stored state name and context and returns a `MigrationResult`. |
| `Differential(Action<IDifferentialBuilder> configure)` | Authors the cross-language differential fuzzing inputs (test-only): per-trigger input samples, per-state seed contexts, and dense probe contexts. Exported into the IR's `differential` block so the differential harness enumerates off the one C# source, with no hand-written machine.json. Only valid on a declaratively-authored machine. See [IDifferentialBuilder](#idifferentialbuilder). |
| `In(TState state)` | Opens a state to declare its context rule and outgoing transitions. Returns an `IStateBuilder`. |

## IStateBuilder

| Method | Description |
|--------|-------------|
| `Holds(Func<JsonObject, string?> validator)` | The state's context rule: return `null` when valid, or a reason string. Enforced on rehydrate and on advance. |
| `Context<TContext>()` | The declarative, string-free replacement for `Holds`: the state's context shape comes from a record. Field names, JSON types, nullability, and attribute constraints (`[MinLength(1)]`) become both the validator and the exportable schema. |
| `Context()` | Declares that the state carries no context (an empty schema). |
| `Requires(Rule constraint)` | A per-state policy layered on the schema (composed, ANDed, chainable): what the state demands beyond its shape, e.g. a complete draft or an absent receipt. Exports as the state's `invariants` entry. Build the rule with the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). |
| `Committed()` | Marks the state as one a soft autosave must not overwrite (except a reset to the initial state). |
| `On(TTrigger trigger)` | Starts a transition out of this state. Returns an `ITransitionBuilder`. |

## ITransitionBuilder

| Method | Description |
|--------|-------------|
| `When(Func<JsonObject, JsonNode?, bool> guard)` | Admits the transition only when the guard passes. The first matching edge wins. |
| `When(Rule guard)` | The declarative, exportable guard: admits the edge only when the `Rule` holds. Build rules with the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). |
| `WithInput<TInput>()` | Declares this trigger's input shape from a record, so the IR carries a typed input schema for it. |
| `Because(string message)` | The detail surfaced when the guard rejects the trigger. |
| `Reduce(Func<JsonObject, JsonNode?, JsonObject> reducer)` | Computes the next context. Return a fresh JSON object; never mutate the input. |
| `Reduce(Reduction reduce)` | The declarative, exportable reducer: `Set(...).FromInput(...)`, `Clear()`, `Reset()`, `Keep()`. See the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). |
| `RunsOnce<TEffect>(string? keyPrefix = null)` | Binds an `ISnapshotEffect` that runs exactly once when this transition is sent, keyed on `{keyPrefix}:{user}:{id}`. Omit `keyPrefix` and it defaults to `{machineId}:{trigger}`. |
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

## Delegate vs declarative

`Holds`, `When(Func...)`, and `Reduce(Func...)` take C# delegates. They run, but they are opaque: a machine
authored with them cannot be exported to the IR that drives cross-language codegen. The declarative overloads
(`Context<T>`, `When(Rule)`, `Reduce(Reduction)`, `WithInput<T>`) express the same validators, guards, and
reducers as data. They compile to the identical engine delegates, so behaviour is unchanged, and they also
record the `DeclarativeModel` that `IrExporter` turns into the machine's `.ir.json`.

The two styles coexist on one builder, so you can migrate a machine edge by edge. But only a fully declarative
machine exports a complete IR: any edge left on a delegate guard or reducer is invisible in the export. Author
the data form with the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules), and see
[Declarative authoring](/docs/statemachine/declarative-authoring) for a machine built end to end.

## Result codes

Every advance and rehydrate returns a typed result, never an exception. See
[Result codes](/docs/sdk-reference/statemachine-api/result-codes) for the full set and when each is returned.
