---
title: IR format
description: "Reference for the state machine IR, the canonical JSON that IrExporter.Export writes from a declarative machine: top-level fields, transitions and an example."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 5
package: Trax.Effect.StateMachine
namespace: Trax.Effect.StateMachine
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/IrExporter.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/SchemaReflection.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/ValueFormats.cs
---

# IR format

`IrExporter.Export(builtMachine)` serializes a declaratively authored machine to its IR: one canonical JSON document
(`<machine>.ir.json`) that carries identity, structure, per-state context schema, per-trigger input schema, and every
transition's guard and reducer as data. The per-language generators read it. The output is
[canonical JSON](/docs/statemachine/canonical-wire), so the same machine always exports the same bytes.
`IMachine.ExportIr()` calls it.

## Signature

```csharp fragment
namespace Trax.Effect.StateMachine;

public static class IrExporter
{
    public static string Export<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        where TState : struct, Enum
        where TTrigger : struct, Enum;
}
```

## Behavior

`Export` throws `InvalidOperationException` if the machine made no declarative call at all (nothing to serialize),
or if it mixes the styles and any edge still has a C# delegate guard or reducer, or any state a `Holds` validator.
The message names each offending edge and state. `Rule.Custom` and `Reduction.Custom` are exported by name. See
[Delegates and declarative rules](/docs/statemachine/delegates-and-declarative-rules).

## Top level

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | string | the machine's stable id |
| `version` | number | the definition version |
| `initialState` | string | the start state |
| `initialContext` | object | the context a new snapshot starts with, from `StartsAt(state, initialContext)`, so a generated runtime reproduces it exactly |
| `states` | string[] | every state, sorted (ordinal) |
| `triggers` | string[] | every trigger, sorted (ordinal) |
| `committedStates` | string[] | states a soft autosave must not overwrite |
| `context` | object | state name to its context schema |
| `inputs` | object | trigger name to its input schema: only what a client may send, so only triggers that declared `WithInput<T>`. A trigger missing here takes no input (or, when effect-bound, the effect's `receipt`); an invoked train's output is under its [outcome](#outcomes) |
| `invariants` | object | state name to a per-state policy rule (the `.Requires(...)` on top of the schema); omitted when the machine has none |
| `transitions` | object[] | the edges, sorted by `(from, trigger, to)` |
| `outcomes` | object | outcome trigger name to its outcome, for states that [invoke a train](#outcomes); omitted when the machine invokes nothing |
| `timers` | object[] | the [timers](#timers) of the timed states, sorted by state; omitted when the machine has none |
| `differential` | object | the test-only fuzzing inputs authored with `.Differential(...)`: `samples` (per trigger), `seeds` (per state), and `contexts` (probes). Omitted when the machine declares none, and stripped from the generated runtime machine (it drives only the cross-language differential test). |

A schema (under `context`, `inputs`, or an outcome's `output`) is
`{ "fields": [ { "name", "type", "nullable", "constraints", "minimum"?, "maximum"?, "enum"?, "enumExact"?, "format"?, "fields"?, "values"?, "keys"? } ] }`,
where `type` is one of `string`/`number`/`integer`/`boolean`/`array`/`object` (`integer` is a number with no
fractional part, decided by its value) and `constraints` is an array of rules. An input's or output's constraints
read the input (`"source": "input"`), a context's read the context. In an input or output schema, a field holding a
record carries that record's `fields`, and so does an array of records, for its elements; a context schema is one
level deep. A runtime validates an input against its schema before any guard reads it, and refuses unknown fields.

An `integer` field carries its CLR type's range as `minimum` and `maximum` (an `int` is
`-2147483648`..`2147483647`, a `byte` `0`..`255`), and a value outside it is refused. Both runtimes read a JSON
number as a double, and past 2^53 two integers can read as one, so a bound that far out is the nearest double inside
the CLR range: a `long` is `-9223372036854774784`..`9223372036854774784`, and the few values between that and
`long.MaxValue` are refused with the values past it, which no runtime can tell apart. A dictionary field (an
`object`) and a list whose elements are not records (an `array`) carry `values`, the schema each value or element
is checked against: `{ "type", "nullable", "minimum"?, "maximum"?, "enum"?, "enumExact"?, "format"?, "fields"?, "values"?, "keys"? }`, so a
`Dictionary<string, int>` refuses `{"a": "one"}` and a `List<long>` refuses `1e19`. Values with no fixed JSON type
(an `object[]`, a `Dictionary<string, object>`) carry no `values` and are any JSON, as a `JsonObject` field is.

A dictionary keyed by anything but a string also carries `keys`, the schema each key is checked against, in the same
shape as `values`, as System.Text.Json reads a key of that type: an integer key is its digits (no leading zero or
plus sign) within the key type's range, compared exactly as text, and an enum, `Guid`, date, time or `char` key
carries its names or its format. A key type no schema can check (a `TimeSpan`, a `bool`, a fractional number, a
`[Flags]` enum, a record) refuses to build, naming the field.

An input schema holds only the properties System.Text.Json sets when it reads the record: one with a public or
`init` setter, a `[JsonInclude]` setter, or a parameter of the constructor it reads with. A computed get-only
property (`int Total => Qty * Price`) is not an input field, so a client that sends it is refused as sending an
unknown field.

An `Int128` and a `UInt128` stop strictly inside ±10^21, because a stored number is written as JavaScript writes it
and an integer from 10^21 on gets an exponent, which those types do not read. A `decimal`, a `float` and a `Half`
carry their CLR range the same way (a `decimal` refuses `1e30`); a `float`'s and a `Half`'s bound is the larger of
the type's maximum and the shortest text that reads back as it (`3.4028235E+38` for a `float`), so the maximum
round-trips. A `double` has none beyond being finite. A `string` field from a CLR enum carries `enum`, the names the
server's JSON writes (camelCase, or a member's `[JsonStringEnumMemberName]`), and a value that is not one of them is
refused. A member's own name is compared ignoring ASCII case; a name given with `[JsonStringEnumMemberName]` is also
listed in `enumExact` and matches only as written, as System.Text.Json reads it. A `[Flags]` enum has no fixed JSON
type and refuses to build, in an input, an output or a context: use a list of an enum without `[Flags]`. A `string`
field whose CLR type gives its text a shape carries `format`:

| `format` | CLR type | A value must be |
| --- | --- | --- |
| `uuid` | `Guid` | 32 hex digits in the hyphenated 8-4-4-4-12 form |
| `date-time` | `DateTime` | `yyyy-MM-ddTHH:mm:ss`, at most seven fraction digits, then nothing, `Z` or an offset of at most 14:00; a real day and time, whose UTC instant (when it has an offset) is inside years 1 to 9999 |
| `date-time-offset` | `DateTimeOffset` | the same, and one with no offset (read in the server's zone) is refused on the range's first and last day, where some zone would push it out |
| `date` | `DateOnly` | `yyyy-MM-dd`, a real day from year 1 |
| `time` | `TimeOnly` | `HH:mm:ss` with at most seven fraction digits |
| `duration` | `TimeSpan` | the ISO-8601 duration a timer reads (`P1DT2H30M`, `PT0.5S`): no years, months, weeks or sign |
| `uri` | `Uri` | absolute, in a form `System.Uri` reads the same on every platform: a scheme of two or more characters; then an authority (a host of dot-separated letter-digit-hyphen labels, a dotted IPv4 address or a bracketed IPv6 one, and an optional port of at most 65535, with no user) and a path, or a path alone; then an optional query and fragment. A `file` URI's host may be empty, and neither it nor a `net.pipe` one has a port; a scheme `System.Uri` parses with rules of its own takes an authority, except `mailto`, which takes one plain address. Every other character is an RFC 3986 unreserved or reserved one, or a `%HH` escape |
| `char` | `char` | one UTF-16 code unit |

Every digit is ASCII. Both runtimes check the same patterns and the same calendar, so a value gets one verdict in
each; where the CLR reader is looser (a relative `Uri`, a time without seconds), the format is stricter.

## Transitions

Each transition carries its structure plus its guard and reducer as data:

| Field | Type | Present when |
| --- | --- | --- |
| `from` / `trigger` / `to` | string | always |
| `guard` | rule | the edge has a declarative guard (`When(Rule)`); absent when the edge has no guard |
| `guardMessage` | string | `Because(...)` was set |
| `reduce` | reduction | the edge has a declarative reducer (`Reduce(Reduction)`); absent when the edge has no reducer, which means the context is kept |
| `effect` | object | the edge binds `RunsOnce<T>`; `{ "type": <TEffect full name>, "keyPrefix": <string> }` |

A rule is a tagged object keyed by `rule` (`present`, `absent`, `ofType`, `nonEmpty`, `oneOf`, `compare`,
`count`, `length`, `boolEquals`, `arrayOf`, `all`, `any`, `custom`); a reduction is keyed by `reduce` (`keep`,
`clear`, `reset`, `set`, `custom`).
See the [data model](/docs/sdk-reference/statemachine-api/declarative-data-model) for each shape.

## Outcomes

A state that [invokes a train](/docs/statemachine/invoking-trains) adds three triggers of their own
kind, named `<State>.done`, `<State>.failed` and `<State>.cancelled`. A trigger enum member cannot contain a dot,
so they never collide with `triggers`, which lists only the machine's own. Each is an entry of `outcomes`:

| Field | Type | Meaning |
| --- | --- | --- |
| `state` | string | the invoking state |
| `outcome` | string | `done`, `failed` or `cancelled` |
| `edges` | object[] | `{ "to", "guard"?, "reduce"? }` in declaration order, which is the order they are tried in; `failed` and `cancelled` have exactly one, without a guard |
| `output` | schema | on `done` only: the schema of the train's output, the success outcome's input |

`failed` and `cancelled` carry no input. A runtime refuses an outcome trigger on `advance` (`outcome-bound`) and
applies it through its outcome entry point (`applyOutcome` in the twin), through its own edges: an output that
does not match `output` is `invalid-input`, the first edge whose guard holds is taken, and when none does the
result is `no-transition`, not `guard-failed`. The run's input
mapping is not exported, and neither is the train's name: the twin never starts a train, and the IR it ships to a
browser does not name the server's train interfaces. `differential.samples` may hold sample outputs under
`<State>.done`. A machine that invokes nothing has no `outcomes` key, so its IR is unchanged.

```json
"outcomes": {
  "Fetching.done": { "state": "Fetching", "outcome": "done",
    "edges": [
      { "to": "NeedsReview",
        "guard": { "rule": "boolEquals", "source": "input", "field": "unsure", "value": true },
        "reduce": { "reduce": "set", "steps": [ { "field": "fingerprint", "value": { "input": "fingerprint" } } ] } },
      { "to": "Fetched",
        "reduce": { "reduce": "set", "steps": [ { "field": "fingerprint", "value": { "input": "fingerprint" } } ] } }
    ],
    "output": { "fields": [
      { "name": "fingerprint", "type": "string", "nullable": false, "constraints": [] },
      { "name": "unsure", "type": "boolean", "nullable": false, "constraints": [] } ] } },
  "Fetching.failed": { "state": "Fetching", "outcome": "failed",
    "edges": [ { "to": "FetchFailed" } ] },
  "Fetching.cancelled": { "state": "Fetching", "outcome": "cancelled",
    "edges": [ { "to": "Cancelled" } ] }
}
```

## Timers

A state declared with `.After(...)` adds a trigger of its own kind, `<State>.after`, which only the server fires. It
is never in `triggers` or `inputs`, which name what a client sends. Each entry of `timers`:

| Field | Type | Meaning |
| --- | --- | --- |
| `state` | string | the timed state |
| `trigger` | string | the timer's trigger, `<State>.after` |
| `target` | string | the state the timer moves the instance to |
| `rules` | string[] | the rules the timer acts under, sorted: the union of the rules of everything that enters the timed state, each written as the [census](/docs/sdk-reference/statemachine-api/add-state-machines#census-and-instruments) writes it (`AnyAuthenticated`, `Anonymous`, `Requires(Editor)`, `Requires(one of A, B)`, `RequiresPolicy(name)`, several joined by ` and `, or `undeclared` for an event that declares none). An event entering the state contributes its rule, the start its rule when the state is the initial one, and a timer or an outcome entering it the rules its own source state was entered under |
| `after` | string | a fixed duration, as an ISO-8601 duration (`PT10M`); present when the timer has one |
| `field` | object | `{ "name", "min", "max" }`: the context field the duration is read from when the state is entered, and its bounds as ISO-8601 durations; present instead of `after` |

```json
"timers": [
  { "state": "Review", "trigger": "Review.after", "target": "Expired",
    "rules": ["AnyAuthenticated", "Requires(Editor)"], "after": "PT10M" }
]
```

A runtime refuses a timer trigger on `advance` (`timer-bound`) and applies it through its timer entry point
(`applyTimer` in the twin). The twin shows the due time and never fires it. A machine with no timer has no `timers`
key, so its IR is unchanged.

## Example

The turnstile, exported (its `differential` block left out):

```json
{
  "id": "turnstile",
  "version": 1,
  "initialState": "Locked",
  "initialContext": {},
  "states": ["Locked", "Unlocked"],
  "triggers": ["Coin", "Push"],
  "committedStates": [],
  "context": {
    "Locked": { "fields": [] },
    "Unlocked": {
      "fields": [
        { "name": "paidWith", "type": "string", "nullable": false,
          "constraints": [ { "rule": "nonEmpty", "source": "context", "field": "paidWith" } ] }
      ]
    }
  },
  "inputs": {
    "Coin": { "fields": [ { "name": "coin", "type": "string", "nullable": false, "constraints": [] } ] }
  },
  "transitions": [
    { "from": "Locked", "trigger": "Coin", "to": "Unlocked",
      "guard": { "rule": "oneOf", "source": "input", "field": "coin", "values": ["quarter", "dollar"] },
      "guardMessage": "Only a quarter or a dollar is accepted.",
      "reduce": { "reduce": "set", "steps": [ { "field": "paidWith", "value": { "input": "coin" } } ] } },
    { "from": "Unlocked", "trigger": "Push", "to": "Locked", "reduce": { "reduce": "clear" } }
  ]
}
```

## See also

- [Declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model), the shapes of a rule and a reduction.
- [Machine](/docs/sdk-reference/statemachine-api/machine#exporting-the-ir), `ExportIr()`.
- [The codegen pipeline](/docs/statemachine/codegen-pipeline), what is generated from the IR.
- [TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package), `IrDocument` and `machineFromIr`.
