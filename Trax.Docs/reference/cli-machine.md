---
title: trax machine
description: "The trax machine commands: scaffold a state machine, export its IR, TypeScript twin and differential corpus, check them for drift, and print its graph."
type: reference
area: tooling
nav_order: 2
source:
  - Trax.Cli/src/Trax.Cli/Commands/MachineCommand.cs
  - Trax.Cli/src/Trax.Cli/Machines
---

# trax machine

The `machine` command group scaffolds a [Tier-1 state machine](/docs/statemachine) and regenerates its
artifacts from the C# source: the [IR](/docs/sdk-reference/statemachine-api/ir-format), the TypeScript twin,
and the differential corpus. It replaces regenerating those by hand (or through a chain of update-flagged
tests), and it is the one command you run after every machine edit. See
[the codegen pipeline](/docs/statemachine/codegen-pipeline) for how the pieces fit together.

The IR is exported in-process from the compiled machine; the twin and corpus are produced by the engine's own
generators, so twin/corpus generation needs `node` (>= 22) on `PATH` and the engine's `src` directory.

```bash
# Scaffold a new machine as one declarative C# file.
trax machine new checkout --output ./Machines --namespace MyApp.Machines --with-effect

# Export the IR, twin, and corpus (each to its own output root).
trax machine generate --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout \
  --engine-src ./vendor/state-machine/src

# Print its states and transitions (or --format mermaid|dot for a diagram source).
trax machine show --assembly ./bin/MyApp.dll

# Fail (exit 1) if any committed artifact is stale (the CI gate).
trax machine check --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout \
  --engine-src ./vendor/state-machine/src
```

## `trax machine new <name>`

Scaffolds one declarative C# file, `<Name>Machine.cs`: the state and trigger enums, a context record, a
guarded transition, and the differential wiring, ready for `trax machine generate`. The machine derives from
`Machine<TState, TTrigger>` in `Trax.Effect.StateMachine.Persistence`, so the project holding the file needs
that package, at the version the CLI prints (the one it is built against). `generate` then reads the built
assembly, not the source.

| Option | Required | Description |
|--------|----------|-------------|
| `<name>` | Yes | Machine name as a kebab-case id (`checkout`, `write-to-congress`). The type prefix is the PascalCase form. |
| `--output` | No | Directory to write `<Name>Machine.cs` (default: current directory). |
| `--namespace` | No | Namespace for the generated file (default: `Machines`). |
| `--with-effect` | No | Include an exactly-once `ISnapshotEffect` stub and mark the terminal state committed. |
| `--force` | No | Overwrite the file if it already exists. |

## `trax machine generate`

Exports the IR from a compiled machine, then generates the twin and/or corpus. Each artifact has its own
output root, because a consumer typically splits them across trees (the IR and corpus in a shared machines
directory, the twin next to the frontend). Pass at least one `--*-out`. The run is atomic and idempotent: every
artifact is staged first, a node step that exits 0 without writing what it should is refused, and the staged
files are then swapped into place together, so a failure at any point, including an output root that cannot
be written, leaves every committed artifact as it was and removes any output directory the run created. If
putting a previous artifact back fails too, the error does not claim nothing changed: it names each artifact
and the `<file>.<token>.bak` path its previous content was left at, to move back by hand.

The assembly is searched for machines among the types that load. A type that cannot (a controller built on
ASP.NET Core, which the CLI does not carry, or a class deriving from a dependency missing from the build
output) is skipped, so a machine can live in a web project; when no machine is found the error lists why the
other types did not load.

The machine's id (what its `Id(...)` sets) names every artifact, so it must be kebab-case: lowercase
letters and digits in words joined by single hyphens, starting with a letter (`checkout`,
`write-to-congress`), the form `trax machine new` produces. `generate` and `check` refuse any other id
before writing anything.

| Option | Required | Description |
|--------|----------|-------------|
| `--assembly` | Yes | Compiled assembly (`.dll`) containing the machine. |
| `--machine` | No | Full type name of the machine. Required only when the assembly has more than one. |
| `--ir-out` | No | Directory to write `<id>.ir.json`. |
| `--twin-out` | No | Directory to write `<id>.contexts.g.ts` and `<id>.machine.g.ts`. |
| `--corpus-out` | No | Directory to write `differential.json`. |
| `--engine-src` | For twin/corpus | The TypeScript engine's `src` directory. |
| `--import-style` | No | Twin engine imports: `relative` (default, for a machine inside the engine repo) or `specifier` (one collapsed import from `--specifier`, for a consumer that vendors the engine behind a path alias). |
| `--specifier` | No | Module specifier used with `--import-style specifier` (default `@trax/state-machine`). |
| `--tools-dir` | No | The engine's `tools/` directory (default: a sibling of `--engine-src`). |
| `--node` | No | Path to the `node` executable (default: `node`). |

## `trax machine check`

Takes the same options as `generate`. It regenerates to a temp location and diffs against what is committed,
printing `ok` / `DRIFT` / `MISSING` per artifact and exiting non-zero on any drift. Because it is the same code
path as `generate`, the two cannot disagree. Wire it into CI to fail a build whose artifacts are stale.

## `trax machine show`

Prints a compiled machine's states and transitions, so a machine can be read or reviewed without its C# or
its IR. It loads the machine exactly as `generate` does.

| Option | Required | Description |
|--------|----------|-------------|
| `--assembly` | Yes | Compiled assembly (`.dll`) containing the machine. |
| `--machine` | No | Full type name of the machine. Required only when the assembly has more than one. |
| `--format` | No | `text` (default), `mermaid` (a `stateDiagram-v2` source) or `dot` (Graphviz). |
| `--ascii` | No | Draw the text format with ASCII only, for a terminal without box or arrow glyphs. |

The text format is one block per state, in the IR's order, with each outgoing edge:

```
checkout  v1   initial: Cart   committed: Paid

Cart ◀ initial
  ── Next ─────▶ Review   guard: Add an item before reviewing.

Paid ■ committed
  ── Restart ──▶ Cart     reduce: reset

Review
  ── Back ─────▶ Cart
  ── Pay ──────▶ Paid     guard: A payable order needs items, a positive total, and a receipt.   reduce: set   ⚡ effect ICheckoutCharge (send only)
```

A guard shows its `Because(...)` text, or its rule when it has none; a reducer shows its kind; an edge that
runs an exactly-once effect shows the effect type and that it is send-only. A state no edge from the initial
state reaches is marked `unreachable from the initial state`, and a state with no outgoing edge prints
`(no transitions)`. Colour is used only when output goes to a terminal and `NO_COLOR` is unset. A control
character in a name or guard message (an escape sequence, a carriage return) is printed as `?`, in every
format, so a machine cannot recolour, clear or retitle the terminal that shows it.

## `trax machine migrate`

Reserved for scaffolding a forward migration by diffing the context schema. Migrations are not yet carried in
the IR (a stored snapshot whose version does not match is rejected and the client starts fresh), so the command
prints that notice to stderr and exits 1, which fails a CI step that runs it rather than reporting a migration
that never happened.

## SDK Reference

> [ExportIr](/docs/sdk-reference/statemachine-api/fluent-authoring#exporting-the-ir) | [IR format](/docs/sdk-reference/statemachine-api/ir-format)
