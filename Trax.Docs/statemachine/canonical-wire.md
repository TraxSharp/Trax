---
title: Canonical wire
description: "The byte-exact serialization of a state machine snapshot that the C# and TypeScript engines both emit: envelope order, key order, numbers and string escapes."
type: reference
area: machines
nav_order: 20
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/CanonicalJson.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/StorableJson.cs
---

# Canonical wire

The canonical wire is the serialization of a snapshot that both engines emit, byte for byte, however the snapshot
was constructed. `CanonicalJson` writes it in C#, and the TypeScript engine writes the same bytes. The exported IR is
written by the same rules, and the differential and migration goldens record their expected snapshots in it.

## Envelope

| Position | Field | Value |
| --- | --- | --- |
| 1 | `machine` | the machine's id |
| 2 | `version` | the definition version |
| 3 | `state` | the state's exact declared name |
| 4 | `context` | the context object, canonicalized as below |

## Context

The context is canonicalized per RFC 8785 (JCS).

| Element | Rule |
| --- | --- |
| Object keys | Sorted by UTF-16 code unit, recursively. Integer-like keys sort as strings (`"10"` before `"2"`). A key named `__proto__` is an ordinary key. |
| Arrays | Element order preserved. |
| Numbers | The ECMAScript `Number::toString` algorithm: `1e21` is written `1e+21`, not .NET's `1E+21`. |
| Strings | Escaped as `JSON.stringify` escapes them: non-ASCII characters and valid surrogate pairs stay literal; control characters use the short escapes or lowercase `\u00xx`; a lone surrogate, which has no UTF-8 encoding, is escaped as lowercase `\udxxx`. |

## Values that have no canonical wire

`Rehydrate` refuses these as `malformed`, and the persistence layer checks an advance's result the same way before
it writes:

| Value | Why |
| --- | --- |
| A number outside the range of a double (`1e400`) | it has no ECMAScript representation |
| A NUL character in a string or a key | a Postgres `jsonb` column refuses it |

## See also

- [State machines](/docs/statemachine#two-runtimes-one-behavior): why both engines must agree byte for byte.
- [IR format](/docs/sdk-reference/statemachine-api/ir-format)
- [The codegen pipeline](/docs/statemachine/codegen-pipeline): the goldens compared over this wire.
