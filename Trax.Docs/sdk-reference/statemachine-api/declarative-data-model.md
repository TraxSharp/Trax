---
title: Declarative data model
description: "Reference for the data types behind declarative state machines: Rule, Reduction, SetStep, ValueSource, the context schema and DeclarativeModel."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 4
package: Trax.Effect.StateMachine
namespace: Trax.Effect.StateMachine
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/Rule.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/Reduction.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/ContextSchema.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/DeclarativeModel.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/RuleEvaluator.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/ReductionEvaluator.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/MachineDefinition.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Fluent.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/StorableJson.cs
---

# Declarative data model

The types a declarative machine records and the IR carries. The [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules)
builds `Rule` and `Reduction` values; `MachineBuilder.Build()` returns them inside a `BuiltMachine`, beside the
engine definition they compile to.

## Rule

A guard or a field constraint, as data. `abstract record Rule` with these cases:

| Case | Fields | Holds when |
| --- | --- | --- |
| `Present` | `Source`, `Field` | the field exists and is not null |
| `Absent` | `Source`, `Field` | the field is missing or null |
| `OfType` | `Source`, `Field`, `Type` | the field is present and of `Type` |
| `NonEmpty` | `Source`, `Field` | a non-empty string or array |
| `OneOf` | `Source`, `Field`, `Values` | a string equal to one of `Values` |
| `Compare` | `Source`, `Field`, `Op`, `Value` (`double`) | a number in the `Op` relation to `Value` |
| `Count` | `Source`, `Field`, `Op`, `Value` (`int`) | an array whose length is in the `Op` relation to `Value` |
| `Length` | `Source`, `Field`, `Op`, `Value` (`int`) | a string whose length is in the `Op` relation to `Value` |
| `BoolEquals` | `Source`, `Field`, `Value` (`bool`) | a boolean field equals `Value` |
| `ArrayOf` | `Source`, `Field`, `ElementType` | an array whose every element is of `ElementType` |
| `All` | `Rules` | every sub-rule holds (empty is true) |
| `Any` | `Rules` | at least one sub-rule holds (empty is false) |
| `Custom` | `Name` | the handler bound with `CustomGuard(name, fn)` resolves it (the escape hatch) |

## Reduction

How a transition computes the destination context, as data. `abstract record Reduction`:

| Case | Fields | Produces |
| --- | --- | --- |
| `Keep` | | the current context, unchanged |
| `Clear` | | an empty context |
| `Reset` | | the machine's initial context |
| `Set` | `Steps` | a clone of the context with each `SetStep` applied |
| `Custom` | `Name` | the result of the handler bound with `CustomReducer(name, fn)` |

### SetStep and ValueSource

A `Set` carries one or more `SetStep(string Field, ValueSource Source)`. `ValueSource` is where the value
comes from:

| Case | Fields | Value |
| --- | --- | --- |
| `FromInput` | `Field` | the named field copied from the trigger input |
| `Constant` | `Value` (`JsonNode?`) | a literal |

## Context schema

Reflected from a context record and carried in the IR.

| Type | Fields | Meaning |
| --- | --- | --- |
| `ContextSchema` | `Fields` | the state's fields, sorted by key (ordinal) to match the canonical wire; `ContextSchema.Empty` is a state with no context |
| `FieldSchema` | `Name`, `Type`, `Nullable`, `Constraints`, `Fields`, `Values`, `Keys`, `Minimum`, `Maximum`, `Enum`, `EnumExact`, `Format` | one field: its JSON key, JSON type, whether it may be null, and any `Rule` constraints (e.g. non-empty from `[MinLength(1)]`). `Fields` is the shape of a nested record (an object field, or each element of an array field) in an input or output schema, null otherwise. `Values` is the schema each value of a dictionary, or each element of a list of non-records, is checked against (a `FieldSchema` with an empty name and no constraints). `Keys` is the schema a dictionary's keys are checked against when its key type is not `string`: an integer key's digits within its range, or an enum's names, or a `Guid`'s, date's, time's or `char`'s format. A dictionary's key type must be a string, an integer, an enum without `[Flags]`, a `Guid`, a date, a time or a `char`; any other refuses to build. `Minimum` and `Maximum` are a number field's CLR range (an integral type's, a `decimal`'s, a `float`'s or a `Half`'s), null for any other field; an `Int128` or `UInt128` is bounded strictly inside ±10^21, and a `float`'s or `Half`'s bound is the shortest literal that reads back as its maximum. `Enum` is the names a field from a CLR enum may hold, as the server's JSON writes them, compared ignoring ASCII case except those in `EnumExact`, the names given with `[JsonStringEnumMemberName]`, which match only as written. A `[Flags]` enum refuses to build in a schema (use a list of an enum without `[Flags]`). `Format` is the shape a string from a `Guid`, date, time, `TimeSpan`, `Uri` or `char` must have (`uuid`, `date-time`, `date-time-offset`, `date`, `time`, `duration`, `uri`, `char`; see [the IR format](/docs/sdk-reference/statemachine-api/ir-format)): a `DateTimeOffset` with no offset is refused on the first and last day of the range. A computed get-only property is not an input field, so a client sending one is refused as an unknown field |

## Enums

| Enum | Values |
| --- | --- |
| `RuleSource` | `Context`, `Input` |
| `JsonFieldType` | `String`, `Number`, `Boolean`, `Array`, `Object`, `Integer` (a number with no fractional part, decided by its value, so `2020.0` is one) |
| `CompareOp` | `GreaterThan`, `GreaterOrEqual`, `LessThan`, `LessOrEqual`, `EqualTo` |

## DeclarativeModel

`BuiltMachine<TState, TTrigger>.Declarative`: the declarative layer of a machine, as data. Null on a machine authored
with delegates only.

| Member | Type | Holds |
| --- | --- | --- |
| `ContextSchemas` | `IReadOnlyDictionary<TState, ContextSchema>` | each state's context schema |
| `TriggerInputs` | `IReadOnlyDictionary<TTrigger, ContextSchema>` | each trigger's input schema, for triggers that declare `WithInput<T>` |
| `Transitions` | `IReadOnlyList<DeclarativeTransition<TState, TTrigger>>` | each edge's `From`, `Trigger`, `To`, `Guard` (`Rule?`, null: always taken) and `Reduce` (`Reduction?`, null: the context is kept) |
| `StateInvariants` | `IReadOnlyDictionary<TState, Rule>` | each state's `Requires(...)` rule |
| `Differential` | `DifferentialModel<TState, TTrigger>?` | the inputs `Differential(...)` authors: `Samples` per trigger, `Seeds` per state and probe `Contexts`; `IsEmpty` is true when none were authored, and the IR then has no `differential` block. Null when the machine declares none |

## BuiltMachine and MachineDefinition

`MachineBuilder<TState, TTrigger>.Build()` returns a `BuiltMachine<TState, TTrigger>`:

| Member | Type | Holds |
| --- | --- | --- |
| `Definition` | `MachineDefinition<TState, TTrigger>` | the engine-ready definition |
| `Engine` | `SnapshotMachine<TState, TTrigger>` | the engine over `Definition` |
| `CommittedStates` | `IReadOnlyCollection<TState>` | the states marked `Committed()` |
| `Effects` | `IReadOnlyList<EffectBinding<TState, TTrigger>>` | the `RunsOnce` binding: `From`, `Trigger`, `To`, `EffectType` (the `TEffect` resolved from the container) and `KeyPrefix` |
| `Declarative` | `DeclarativeModel<TState, TTrigger>?` | the declarative layer above |

`MachineDefinition<TState, TTrigger>` is what `SnapshotMachine` runs:

| Member | Type | Holds |
| --- | --- | --- |
| `Id`, `Version` | `string`, `int` | the machine's id and definition version |
| `InitialState` | `TState` | the start state |
| `CreateInitialContext` | `Func<JsonObject>` | the factory `StartsAt` was given; `CreateInitialSnapshot()` calls it |
| `Transitions` | `IReadOnlyList<TransitionDefinition<TState, TTrigger>>` | each edge's `From`, `Trigger`, `To`, `Guard`, `GuardMessage` and `Reduce`, as delegates |
| `ContextValidators` | `IReadOnlyDictionary<TState, Func<JsonObject, string?>>` | each state's context rule: null when valid, else the reason |
| `Migrations` | `IReadOnlyDictionary<int, Func<string, JsonObject, MigrationResult>>` | the `MigrateFrom` steps, keyed by the version each upgrades |

`SnapshotMachine<TState, TTrigger>` offers `Advance`, `CanFire`, `AvailableTriggers`, `Rehydrate`, `Serialize` and
`Describe`; each answers with a result, never an exception (see [Result codes](/docs/sdk-reference/statemachine-api/result-codes)).

## Behavior

- `RuleEvaluator.Evaluate(rule, context, input, customGuards)` is total: it returns `false` for a missing or
  wrong-typed field, never an exception. A `Custom` name with no handler in the map is also `false`, but a machine
  built with `MachineBuilder` cannot reach that case: `Build()` refuses a custom name that has no handler bound.
- `ReductionEvaluator.Apply(reduction, context, input, initialContext, customReducers)` always returns a fresh
  `JsonObject`; it never mutates the inputs.
- `StorableJson.Problem(node)` names the first value a snapshot context may not carry although it is valid JSON (a
  number outside the range of a double, a NUL character in a string or a key), or returns null. It never throws.
  `Rehydrate` refuses both as `malformed`, and the persistence layer checks an advance's result the same way before
  it writes.

## See also

- [Rules](/docs/sdk-reference/statemachine-api/rules), the vocabulary that builds these.
- [IR format](/docs/sdk-reference/statemachine-api/ir-format), how they are written out.
- [Machine](/docs/sdk-reference/statemachine-api/machine).
