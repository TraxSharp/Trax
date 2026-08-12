---
layout: default
title: IR format
parent: State Machine API
grand_parent: SDK Reference
nav_order: 5
---

# IR format

`IrExporter.Export(builtMachine)` serializes a declaratively-authored machine to its IR: one canonical JSON
document (`<machine>.ir.json`) that carries identity, structure, per-state context schema, per-trigger input
schema, and every transition's guard and reducer as data. It is the single artifact the per-language
generators consume, so the C# machine is the source and the IR is the contract. Output is
[canonical JSON](/docs/statemachine#the-canonical-wire), so the file is a stable golden.

Export requires a declarative machine: `Export` throws if the machine was authored with delegates only
(nothing to serialize).

## Top level

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | string | the machine's stable id |
| `version` | number | the definition version |
| `initialState` | string | the start state |
| `states` | string[] | every state, sorted (ordinal) |
| `triggers` | string[] | every trigger, sorted (ordinal) |
| `committedStates` | string[] | states a soft autosave must not overwrite |
| `context` | object | state name to its context schema |
| `inputs` | object | trigger name to its input schema (only triggers that declared `WithInput<T>`) |
| `invariants` | object | state name to a per-state policy rule (the `.Requires(...)` on top of the schema); omitted when the machine has none |
| `transitions` | object[] | the edges, sorted by `(from, trigger, to)` |

A schema (under `context` or `inputs`) is `{ "fields": [ { "name", "type", "nullable", "constraints" } ] }`,
where `type` is one of `string`/`number`/`boolean`/`array`/`object` and `constraints` is an array of rules.

## Transitions

Each transition carries its structure plus its guard and reducer as data:

| Field | Type | Present when |
| --- | --- | --- |
| `from` / `trigger` / `to` | string | always |
| `guard` | rule | the edge has a declarative guard |
| `guardMessage` | string | `Because(...)` was set |
| `reduce` | reduction | the edge has a declarative reducer |
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
