---
layout: default
title: Rules vocabulary
parent: State Machine API
grand_parent: SDK Reference
nav_order: 3
---

# Rules vocabulary

`Rules` is the string-free authoring surface over the declarative `Rule` and `Reduction` data. Import it and
reference fields by member expression, so a guard reads as one English-shaped line and still compiles to
inspectable data the machine can export to its IR.

```csharp
using static Trax.Effect.StateMachine.Rules;
```

A field is selected with `Input(...)` (the trigger input) or `Field(...)` (the snapshot context), then a
terminal method produces the `Rule` or `Reduction`:

```csharp
.When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
.Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
```

The selector (`i => i.Coin`) is an expression tree, resolved to its JSON key (camelCase). There are no
field-name strings, so a rename refactors the rule with the record.

## Field selectors

| Method | Returns | Selects |
| --- | --- | --- |
| `Input<TInput, TField>(Expression<Func<TInput, TField>> selector)` | `FieldMatcher` | a field on the trigger input |
| `Field<TContext, TField>(Expression<Func<TContext, TField>> selector)` | `FieldMatcher` | a field on the snapshot context |

## Guard predicates

Terminal methods on `FieldMatcher`, each returning a `Rule`:

| Method | Holds when |
| --- | --- |
| `Present()` | the field exists and is not null |
| `Absent()` | the field is missing or null |
| `NonEmpty()` | a non-empty string or array |
| `IsOneOf(params string[] values)` | a string equal to one of `values` |
| `OfType(JsonFieldType type)` | the field is present and of that JSON type |
| `GreaterThan(double value)` | a number greater than `value` |
| `EqualTo(double value)` | a number equal to `value` |
| `CountGreaterThan(int value)` | an array with more than `value` elements |
| `CountAtLeast(int value)` | an array with at least `value` elements |

Every predicate is total: a missing or wrong-typed field is `false`, never an exception. That is what lets
guards run against an in-progress draft without throwing.

## Combinators

| Method | Returns | Holds when |
| --- | --- | --- |
| `All(params Rule[] rules)` | `Rule` | every sub-rule holds (an empty set is vacuously true) |
| `Any(params Rule[] rules)` | `Rule` | at least one sub-rule holds (an empty set is false) |

```csharp
.When(All(
    Field((CartContext c) => c.Items).CountAtLeast(1),
    Field((CartContext c) => c.Total).GreaterThan(0)))
```

## Reductions

A reducer computes the destination context. Build one with these factories:

| Factory | Produces |
| --- | --- |
| `Keep()` | the current context, unchanged (the default when no reducer is declared) |
| `Clear()` | an empty context |
| `Reset()` | the machine's initial context |
| `Set<TContext, TField>(Expression<Func<TContext, TField>> field)` | a `SetBuilder`; complete it below |

`Set(...)` clones the context and assigns one field:

| Completion | Sets the field to |
| --- | --- |
| `.FromInput<TInput, TField>(Expression<Func<TInput, TField>> selector)` | a field copied from the trigger input |
| `.ToValue(JsonNode? value)` | a literal JSON value |

## Beyond the vocabulary

The vocabulary covers the common guards and reducers on purpose: a small, fixed set keeps the exported IR a
declarative contract rather than a serialized-logic DSL. Logic that does not fit (a formula reducer, a
multi-field invariant) stays on the delegate overloads, `When(Func...)` and `Reduce(Func...)`. Those run
identically, but an edge left on a delegate does not appear in the machine's IR, so a machine that needs a
complete export keeps its custom cases to a minimum.
