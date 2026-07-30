# @trax/state-machine

The TypeScript half of the Trax portable snapshot state-machine: a small engine that represents a
multi-step flow's state as language-neutral JSON so a browser client and a C# backend
(`Trax.Effect.StateMachine`) agree on it. Illegal `(state, context)` pairs are unrepresentable and every
operation is total (it never throws).

This repo owns three things:

| Piece | Location |
| --- | --- |
| The `@trax/state-machine` package (engine, and later the typed facade + React hook) | [`src/`](src) |
| The shared, language-neutral fixtures both engines drive (the cross-language oracle) | [`machines/`](machines) |
| The one generator that emits per-language structure from `machine.json` | [`tools/state-machine-codegen/`](tools/state-machine-codegen) |

## The contract

A **snapshot** is the only thing that crosses the wire or lands in a row:

```json
{ "machine": "turnstile", "version": 1, "state": "Locked", "context": {} }
```

- `state` is the current step (a string-literal union member).
- `context` is the per-state data, discriminated by `state`.

Two engines (this one and the C# one) implement the same behavior. They are kept identical not by
generating one from the other but by a shared **conformance fixture** suite under `machines/<machine>/`:
both suites enumerate the same files and must produce the same outcome. Only the result *codes*
(`no-transition`, `guard-failed`, `invalid-context`, `malformed`, ...) are contract; human-readable detail
text is free to differ.

## Structure is generated, behavior is hand-written

`machine.json` is the single source of truth for a machine's **structure** (states, triggers, edges, guard
messages). The generator emits the per-language structure (`*.g.ts` here, `*.g.cs` in Trax) from it;
guards and reducers are hand-written per language and bound to the generated edges by **name**. The
snapshot carries structure and data, never logic.

```bash
npm run codegen         # write the generated files
npm run codegen:check   # CI: fail on drift
npm test                # vitest: conformance, totality, serialization, migration, codegen drift
```

## Status

The turnstile proof machine is complete on both runtimes and proven in agreement by the shared fixtures.
The typed facade (`TypedMachine`), the framework-free controller, and the `useMachine` React hook land in
a later phase; the effectful `checkout` demo (multi-key context, a branch, an exactly-once effect) lands
alongside the Trax.Samples end-to-end.
