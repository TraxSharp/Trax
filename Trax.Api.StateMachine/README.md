# @trax/state-machine

The TypeScript half of the Trax portable snapshot state-machine: a small engine that represents a
multi-step flow's state as language-neutral JSON so a browser client and a C# backend
(`Trax.Effect.StateMachine`) agree on it. Illegal `(state, context)` pairs are unrepresentable and every
operation is total (it never throws).

This repo owns three things:

| Piece | Location |
| --- | --- |
| The `@trax/state-machine` package (engine, rule interpreter, typed facade, React hook) | [`src/`](src) |
| The shared, language-neutral fixtures both engines drive (the cross-language oracle) | [`machines/`](machines) |
| The IR-driven generators (`generateContextTypes` / `generateMachineFactory`) and the legacy node generator | [`src/rules/generateTypes.ts`](src/rules/generateTypes.ts), [`tools/state-machine-codegen/`](tools/state-machine-codegen) |

## The contract

A **snapshot** is the only thing that crosses the wire or lands in a row:

```json
{ "machine": "turnstile", "version": 1, "state": "Locked", "context": {} }
```

- `state` is the current step (a string-literal union member).
- `context` is the per-state data, discriminated by `state`.

Two engines (this one and the C# one) implement the same behavior. They are kept identical by a shared
**differential corpus** under `machines/<machine>/`: one runtime enumerates every reachable case into a
committed golden and every other runtime replays it and must produce the same outcome. Only the result
*codes* (`no-transition`, `guard-failed`, `invalid-context`, `malformed`, ...) are contract; human-readable
detail text is free to differ. Byte-exact wire is guaranteed by a full RFC 8785 (JCS) canonical serializer in
each runtime, pinned by a shared conformance-vector suite.

## Data is generated, logic is interpreted

**C# is the source of truth.** A machine is authored declaratively in C# (`Trax.Effect.StateMachine`), and
`IrExporter.Export` emits a neutral, versioned **IR** (`<machine>.ir.json`) carrying identity, structure, the
per-state context/input schemas, the declarative guards/reducers and invariants, committed states, effect
bindings, and the differential seeds.

The generators here turn that IR into the frontend twin: `generateContextTypes` emits the state/trigger/context
types, and `generateMachineFactory` emits a typed factory that embeds the IR and constructs the running machine
via `machineFromIr(ir)`. There is **no per-language logic to hand-write** for the ~92% of guards/reducers the
declarative vocabulary covers: they are data in the IR, run by the rule interpreter (`src/rules/interpreter.ts`
here, `RuleEvaluator` in C#). A genuinely-custom guard/reducer is bound by *name* in the IR and hand-written
per runtime (the ~8% escape hatch); the migrated machines have none.

The generated files carry an `AUTO-GENERATED / Do not edit by hand` banner; any hand-written UI glue lives in a
separate sibling *bridge* file. Regeneration and the drift check share one code path, so they cannot disagree.

```bash
npm test                # vitest: conformance, totality, serialization, migration, codegen drift
```

Consumers drive regeneration through `trax machine generate` (the Trax.Cli `machine` command group, in
progress) or, on the C# side, the exporter directly. The legacy `tools/state-machine-codegen/generate.mjs`
reads a hand-authored `machine.json` and still serves the one machine not yet migrated (`checkout`); it is
retired for everything on the IR pipeline.

## Status

The `turnstile` proof machine and the `write-to-congress` product machine (the first real consumer, in nwyc)
are on the IR pipeline and proven in agreement by the differential. `checkout` is the last machine still on
the legacy hand-written twin + `machine.json`, pending the CLI increment. TypeScript is still the differential
oracle; the flip to a C# enumerator is a scheduled increment. See
[`docs/design/tier1-machine-codegen.md`](docs/design/tier1-machine-codegen.md) and
[`docs/design/phase0-codegen-scope.md`](docs/design/phase0-codegen-scope.md).
