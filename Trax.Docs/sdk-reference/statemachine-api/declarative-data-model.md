---
layout: default
title: Declarative data model
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
| `Custom` | `Name` | a named handler resolves it (the escape hatch) |

Evaluation is total: `RuleEvaluator` returns `false` for a missing or wrong-typed field, never an exception.

## Reduction

How a transition computes the destination context, as data. `abstract record Reduction`:

| Case | Fields | Produces |
| --- | --- | --- |
| `Keep` | | the current context, unchanged |
| `Clear` | | an empty context |
| `Reset` | | the machine's initial context |
| `Set` | `Steps` | a clone of the context with each `SetStep` applied |
| `Custom` | `Name` | the result of a named handler |

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
| `FieldSchema` | `Name`, `Type`, `Nullable`, `Constraints` | one field: its JSON key, JSON type, whether it may be null, and any `Rule` constraints (e.g. non-empty from `[MinLength(1)]`) |

## Enums

| Enum | Values |
| --- | --- |
| `RuleSource` | `Context`, `Input` |
| `JsonFieldType` | `String`, `Number`, `Boolean`, `Array`, `Object` |
| `CompareOp` | `GreaterThan`, `GreaterOrEqual`, `LessThan`, `LessOrEqual`, `EqualTo` |
