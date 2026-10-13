---
layout: default
title: Declarative data model
description: "Reference for the data types behind declarative state machines: Rule, Reduction, SetStep and ValueSource, the context schema and the enums."
parent: State Machine API
grand_parent: SDK Reference
nav_order: 4
---

# Declarative data model

The [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules) is sugar over these types. You rarely
construct them by hand, but they are what a declarative machine records and what the IR carries, so this is
the reference for reading an export or writing a generator.

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

Evaluation is total: `RuleEvaluator` returns `false` for a missing or wrong-typed field, never an exception.
A `Custom` name with no handler in the map is also `false`, but a machine built with `MachineBuilder` cannot
reach that case: `Build()` refuses a custom name that has no handler bound.

## Reduction

How a transition computes the destination context, as data. `abstract record Reduction`:

| Case | Fields | Produces |
| --- | --- | --- |
| `Keep` | | the current context, unchanged |
| `Clear` | | an empty context |
| `Reset` | | the machine's initial context |
| `Set` | `Steps` | a clone of the context with each `SetStep` applied |
| `Custom` | `Name` | the result of the handler bound with `CustomReducer(name, fn)` |

`ReductionEvaluator` always returns a fresh `JsonObject`; it never mutates the inputs.

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
