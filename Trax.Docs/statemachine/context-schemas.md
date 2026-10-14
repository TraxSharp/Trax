---
title: Context and input schemas
description: "How Context<T>() and WithInput<T>() turn a C# record into a state machine schema: JSON types per CLR type, field names, constraints and refusals."
type: reference
area: machines
nav_order: 21
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/SchemaReflection.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/ContextSchema.cs
---

# Context and input schemas

`Context<T>()` declares a state's context schema from a record, and `WithInput<T>()` a trigger's input schema. Both
reflect the record when the machine is configured. The schema is the state's validator on the server and is
exported in the [IR](/docs/sdk-reference/statemachine-api/ir-format); its data model is under
[Context schema](/docs/sdk-reference/statemachine-api/declarative-data-model#context-schema).

## JSON types

Each field's JSON type is the one System.Text.Json writes for its CLR type, so a record validates against its own
camelCase JSON.

| CLR type | JSON type |
| --- | --- |
| `string`, `char`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri`, an enum without `[Flags]` | string (an enum under `JsonStringEnumConverter`) |
| every integral type, signed or unsigned, including `Int128` and `UInt128` | integer (a number with no fractional part) |
| `float`, `double`, `decimal`, `Half` | number |
| `bool` | boolean |
| `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `JsonObject`, a record or class | object |
| any other `IEnumerable`, `JsonArray` | array, with an element-type constraint when the element is one of the scalars above |

## Field names

| Property | Field |
| --- | --- |
| with `[JsonPropertyName]` | that name |
| without | its name under `JsonNamingPolicy.CamelCase` (`PaidWith` is `paidWith`, `ID` is `id`, `URLValue` is `urlValue`) |
| with `[JsonIgnore]` | not a field |
| computed (get-only, never set on read) | not an input field; a client sending one is refused as an unknown field |

## Constraints

| Source | Constraint |
| --- | --- |
| `[MinLength(1)]` (or more) on a string or a collection | non-empty |
| `[MaxLength(n)]` on a string or a collection | length or count at most `n`, checked when the value is present (a `Length` or `Count` rule with `LessOrEqual`) |
| an integral type | its CLR range: an `int` field refuses `1e10`. An integer within one double step of a 64-bit (or wider) type's end is refused too, since an engine reading numbers as doubles cannot tell it from one past the end. |
| a dictionary's values, a list's elements | checked against their own type: a `Dictionary<string, int>` refuses `{"a": "one"}` |
| a nullable annotation (`string?`) or nullable value type (`int?`) | the field may be null |

The full constraint set (formats, enum names, dictionary keys) is listed under
[the IR format](/docs/sdk-reference/statemachine-api/ir-format).

## Depth

| Schema | Described |
| --- | --- |
| A context (`Context<T>()`) | one level deep |
| A trigger input (`WithInput<T>()`) and an invoked train's output | as strictly as the CLR type: a nested record and the elements of a list of records carry their own field shapes |

Both engines refuse a trigger input that does not match its schema as `invalid-input`, before any guard reads it. A
trigger declared without `WithInput` takes no input. See
[Trigger input](/docs/sdk-reference/statemachine-api/machine#trigger-input).

## Refusals

`Context<T>()` and `WithInput<T>()` throw `InvalidOperationException`, naming the property, when the machine is
configured, for:

| Property or type | Reason |
| --- | --- |
| a property with no fixed JSON type: `object`, `JsonNode`, `JsonElement`, `BigInteger`, a delegate | its values would not match any one JSON type |
| a `[Flags]` enum | use a list of an enum without `[Flags]` |
| a record that contains itself | it has no finite shape |

A state whose context cannot be described this way is validated with `Holds(...)`, which makes the machine
non-exportable, or with a custom rule (`Rule.Custom`), which keeps it exportable.

## See also

- [Write guards and reducers as data](/docs/statemachine/declarative-authoring)
- [Declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model)
- [Machine authoring](/docs/sdk-reference/statemachine-api/machine)
