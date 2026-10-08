---
layout: default
title: IR format
description: "Reference for the state machine IR, the canonical JSON that IrExporter.Export writes from a declarative machine: top-level fields, transitions and an example."
parent: State Machine API
grand_parent: SDK Reference
nav_order: 5
---

# IR format

`IrExporter.Export(builtMachine)` serializes a declaratively-authored machine to its IR: one canonical JSON
document (`<machine>.ir.json`) that carries identity, structure, per-state context schema, per-trigger input
schema, and every transition's guard and reducer as data. It is the single artifact the per-language
generators consume, so the C# machine is the source and the IR is the contract. Output is
[canonical JSON](/docs/statemachine#two-runtimes-one-behavior), so the file is a stable golden.

Export requires a declarative machine: `Export` throws `InvalidOperationException` if the machine made no
declarative call at all (nothing to serialize), or if it mixes the styles and any edge still has a C# delegate
guard or reducer, or any state a `Holds` validator. The IR cannot carry a delegate, and an edge without its
`guard` or `reduce` would read as an unconditional edge that keeps the context. The message names each
offending edge and state; `Rule.Custom` and `Reduction.Custom` keep such logic exportable. See
[Delegate vs declarative](/docs/sdk-reference/statemachine-api/fluent-authoring#delegate-vs-declarative).

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
| `inputs` | object | trigger name to its input schema (only triggers that declared `WithInput<T>`) |
| `invariants` | object | state name to a per-state policy rule (the `.Requires(...)` on top of the schema); omitted when the machine has none |
| `transitions` | object[] | the edges, sorted by `(from, trigger, to)` |
| `differential` | object | the test-only fuzzing inputs authored with `.Differential(...)`: `samples` (per trigger), `seeds` (per state), and `contexts` (probes). Omitted when the machine declares none, and stripped from the generated runtime machine (it drives only the cross-language differential test). |

A schema (under `context` or `inputs`) is `{ "fields": [ { "name", "type", "nullable", "constraints" } ] }`,
where `type` is one of `string`/`number`/`boolean`/`array`/`object` and `constraints` is an array of rules.

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

## Example

The turnstile, exported:

```json
{
  "id": "turnstile",
  "version": 1,
  "initialState": "Locked",
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
