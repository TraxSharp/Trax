---
title: Rules
description: "Reference for the Rules vocabulary: field selectors, guard predicates, combinators and reductions that author declarative state machine logic as data."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 3
package: Trax.Effect.StateMachine
namespace: Trax.Effect.StateMachine
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/Rules.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/MemberPath.cs
---

# Rules

`Rules` is the string-free authoring surface over the declarative [`Rule` and `Reduction`
data](/docs/sdk-reference/statemachine-api/declarative-data-model). A field is selected with `Input(...)` (the trigger
input) or `Field(...)` (the snapshot context) by member expression, and a terminal method on the `FieldMatcher`
produces the `Rule`; the reduction factories produce a `Reduction`.

## Signature

```csharp fragment
namespace Trax.Effect.StateMachine;

public static class Rules
{
    public static FieldMatcher Input<TInput, TField>(Expression<Func<TInput, TField>> selector);
    public static FieldMatcher Field<TContext, TField>(Expression<Func<TContext, TField>> selector);
    public static Rule All(params Rule[] rules);
    public static Rule Any(params Rule[] rules);
    public static Reduction Keep();
    public static Reduction Clear();
    public static Reduction Reset();
    public static SetBuilder Set<TContext, TField>(Expression<Func<TContext, TField>> field);
    public static Reduction SetAll(params Reduction[] sets);
}
```

## Members

### Field selectors

| Method | Returns | Selects |
| --- | --- | --- |
| `Input<TInput, TField>(Expression<Func<TInput, TField>> selector)` | `FieldMatcher` | a field on the trigger input |
| `Field<TContext, TField>(Expression<Func<TContext, TField>> selector)` | `FieldMatcher` | a field on the snapshot context |

### Guard predicates

Terminal methods on `FieldMatcher`, each returning a `Rule`:

| Method | Holds when |
| --- | --- |
| `Present()` | the field exists and is not null |
| `Absent()` | the field is missing or null |
| `NonEmpty()` | a non-empty string or array |
| `IsOneOf(params string[] values)` | a string equal to one of `values` |
| `OfType(JsonFieldType type)` | the field is present and of that JSON type |
| `GreaterThan(double value)` | a number greater than `value` |
| `AtLeast(double value)` | a number greater than or equal to `value` |
| `LessThan(double value)` | a number less than `value` |
| `AtMost(double value)` | a number less than or equal to `value` |
| `EqualTo(double value)` | a number equal to `value` |
| `CountGreaterThan(int value)` | an array with more than `value` elements |
| `CountAtLeast(int value)` | an array with at least `value` elements |
| `LengthGreaterThan(int value)` | a string whose length is more than `value` |
| `LengthAtLeast(int value)` | a string whose length is at least `value` |
| `IsTrue()` / `IsFalse()` | a boolean field equal to `true` / `false` |
| `ArrayOf(JsonFieldType type)` | an array whose every element is of `type` |

The numeric comparisons export as a `compare` rule whose `op` is `gt`, `gte`, `lt`, `lte` or `eq`
([the IR format](/docs/sdk-reference/statemachine-api/ir-format)). There is no negation rule: write the
opposite comparison (`AtMost(12)` for "not greater than 12"). The two differ on a field that is missing, null or
not a number: `AtMost(12)` is false there, where "not greater than 12" would be true. To accept a missing field as
well, write `Any(Field(...).Absent(), Field(...).AtMost(12))`.

A comparison reads a number, so `Build` refuses one on a field whose schema type is not `number` or `integer`
(an enum, a string, a `DateTime` or `DateTimeOffset`, which are strings on the wire), naming the field and its
type; a nullable number is fine. The bound must be finite: `double.NaN` or an infinity is refused when the rule is
written. Both runtimes compare as IEEE doubles, so a decimal value is compared as the nearest double
(`99.99999999999999999` reads as `100`).

### Combinators

| Method | Returns | Holds when |
| --- | --- | --- |
| `All(params Rule[] rules)` | `Rule` | every sub-rule holds (an empty set is vacuously true) |
| `Any(params Rule[] rules)` | `Rule` | at least one sub-rule holds (an empty set is false) |

### Reductions

A reducer computes the destination context. Build one with these factories:

| Factory | Produces |
| --- | --- |
| `Keep()` | the current context, unchanged (the default when no reducer is declared) |
| `Clear()` | an empty context |
| `Reset()` | the machine's initial context |
| `Set<TContext, TField>(Expression<Func<TContext, TField>> field)` | a `SetBuilder`; complete it below |
| `SetAll(params Reduction[] sets)` | one reduction that applies every `Set` it is given, in order. Throws `ArgumentException` for a reduction that is not a `Set`, or a field set twice |

`Set(...)` clones the context and assigns one field:

| Completion | Sets the field to |
| --- | --- |
| `.FromInput<TInput, TField>(Expression<Func<TInput, TField>> selector)` | a field copied from the trigger input |
| `.ToValue(JsonNode? value)` | a literal JSON value |

A transition or an outcome edge takes one reduction, so one that fills several fields combines its `Set`s with
`SetAll`:

```csharp fragment
.OnDone(Played.Turn, reduce: SetAll(
    Set((TurnContext c) => c.Slot).FromInput((PlayResult r) => r.Slot),
    Set((TurnContext c) => c.Target).FromInput((PlayResult r) => r.Target)))
```

## Behavior

- A selector (`i => i.Coin`) is an expression tree, resolved to the member's JSON key: its `[JsonPropertyName]` when it has
  one, else its name in camelCase. A rename of the record's member renames the rule with it.
- Every predicate is total: a missing or wrong-typed field is `false`, never an exception.
- The vocabulary is fixed. Logic outside it is `new Rule.Custom(name)` or `new Reduction.Custom(name)`, records you construct, with a handler bound by
  `CustomGuard` or `CustomReducer`; see [Delegates and declarative rules](/docs/statemachine/delegates-and-declarative-rules).

## Examples

```csharp compile
var guard = All(
    Field((CheckoutContext c) => c.Items).CountAtLeast(1),
    Field((CheckoutContext c) => c.Receipt).Absent());

var reduce = Set((PaidContext p) => p.Receipt).FromInput((PayInput i) => i.Receipt);
```

On a transition, from the State Machine sample:

```csharp from=Trax.Samples/samples/StateMachine/Trax.Samples.StateMachine/Machines.cs
.When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
.Because("Only a quarter or a dollar is accepted.")
.Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
```

## See also

- [Declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model), the types these build.
- [Machine](/docs/sdk-reference/statemachine-api/machine#itransitionbuilder), where a rule and a reduction go.
- [Delegates and declarative rules](/docs/statemachine/delegates-and-declarative-rules).
- [Declarative authoring](/docs/statemachine/declarative-authoring).
