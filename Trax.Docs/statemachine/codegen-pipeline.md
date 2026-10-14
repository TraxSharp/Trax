---
title: The codegen pipeline
description: "How a state machine's TypeScript twin is generated from its C# definition through the IR, and how the differential corpus and migration golden prove they agree."
type: explanation
area: machines
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules/IrExporter.cs
  - Trax.Effect/src/Trax.Effect.StateMachine/CorpusReplay.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Testing
---

# The codegen pipeline

A machine has two runtimes, the C# engine on the server and a TypeScript twin in the browser, and they must
agree down to the byte. Writing both by hand means keeping two implementations in sync forever. The pipeline
removes the second one: the machine is authored once in C#, and everything the frontend needs is generated from
it.

## The flow

```
C# machine (the source)
   -> IrExporter.Export  ->  <machine>.ir.json   (the IR: structure + guards + reducers as data)
        -> generators    ->  state / trigger types
                             context type per state
                             shape validators
                             a runnable, typed machine
```

The [IR](/docs/sdk-reference/statemachine-api/ir-format) is the interchange contract. Because a
[declaratively authored](/docs/statemachine/declarative-authoring) machine records its guards, reducers, and
context schema as data, the export is complete: a generator has everything it needs to build a working twin,
not just the state diagram. The TypeScript side reads the same rule data and evaluates it through a small
interpreter that mirrors the C# evaluators, so the generated machine runs exactly the rules the C# source
declared. There is no hand-written binding layer.

## What you write and what is generated

Per machine, you write one C# file: the state and trigger enums, the context and input records, and the
`Configure` method. The state and trigger types, the context types, the validators and the runnable typed machine
are generated from that file's IR. The only thing hand-written per frontend is the UI that drives the machine.

How much of that holds depends on how much of the machine fits the
[Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). Export refuses a machine that still has an edge on a
delegate guard or reducer, or a state on a `Holds` validator, because the IR cannot carry it and the generated twin
would disagree with the server on that edge. Logic the vocabulary cannot express goes in a custom rule or reduction
(`Rule.Custom`, `Reduction.Custom`), which the IR names and each runtime binds a handler for. A machine that stays
declarative generates its whole twin.

## The differential corpus

Agreement on structure is not enough. A custom guard or reducer exists once per language, and the interpreters that
run the declarative rules must agree too, so behaviour can still drift. The differential corpus catches that.

TypeScript is the oracle. It drives the engine over every reachable snapshot, discovered by walking the machine's
own transitions from the initial snapshot plus a few declared seeds for states whose context arrives out of band,
times every trigger, times a few representative samples per trigger. Each outcome is recorded as canonical wire on
a transition, or as a rejection code. The result is committed as `machines/<machine>/differential.json`, and both
engines replay it and must reproduce every outcome byte for byte. The turnstile machine's corpus is 18 cases and the
checkout's 30, most of them rejections, which is what a hand-written fixture set never covers exhaustively.

The corpus is generated, never hand-written. Its inputs are authored in C#, in `Configure`, and exported into the
IR's `differential` block, so the corpus is driven off the one C# source:

```csharp fragment
m.Differential(d => d
    .Sample(CheckoutTrigger.Pay, new JsonObject { ["receipt"] = "rcpt_1" })
    .EmptySample(CheckoutTrigger.Pay)
    .Seed(CheckoutState.Review, new JsonObject
    {
        ["items"] = new JsonArray("book"),
        ["total"] = 999,
        ["receipt"] = null,
    }));
```

- **Samples** are a few representative inputs per trigger; a no-input case is always added. A guard that accepts
  `quarter` or `dollar` wants one input that passes, one that fails, and an empty one.
- **Seeds** are a representative valid context for a state no trigger reaches (context that arrives through
  autosave rather than a transition). The initial state and everything reachable from it need none. A state may
  take several seeds, one per context worth starting from, and the corpus expands each of them; the same context
  twice for one state is refused at `Build`. A probe is different: it is crossed with every state once and never
  expanded, so it checks guards and validators where it lands but reaches nothing beyond.
- **Probes** and **outcome samples** cover snapshots a client could send but no walk reaches, and the outputs of an
  invoked train.

[IDifferentialBuilder](/docs/sdk-reference/statemachine-api/machine#idifferentialbuilder) lists the
overloads. Regenerating the corpus is deliberate, and the git diff of the golden is the review of what changed. Each
side replays the committed file on its own, so the C# suite needs no Node and the TypeScript suite needs no .NET.

## The migration golden

When a machine bumps its `version`, a per-source-version migration upgrades an older stored snapshot on rehydrate
(see [Migrations](/docs/sdk-reference/statemachine-api/migrations)); a missing step in the chain is a typed
`version-mismatch`, never a silent misread. Correctness against real stored shapes is pinned by the migration
golden, `machines/<machine>/migration.json`: a committed set of stored older-version snapshots and the exact
canonical wire each must become. Both engines replay it, so a migration that drops, renames, or reorders a surviving
field fails, and the two runtimes cannot migrate the same draft differently.

The differential guards machine logic; the migration golden guards schema evolution. Because both are byte-exact
comparisons over the [canonical wire](/docs/statemachine/canonical-wire), a divergence is a hard failure, not a
judgement call.

## Generation and drift

One command runs the whole flow, [`trax machine generate`](/docs/reference/cli#trax-machine-generate): it exports the
IR from the compiled machine in-process, then drives the engine's own generators to emit the twin and the corpus,
each to its own output root. It reads only the IR it exports from the compiled machine, so there is no `machine.json`
to edit. The generators ship inside the tool with the engine compiled in, so this needs only `node` (>= 22):

```bash
trax machine generate --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout
```

The twin imports [`@traxsharp/state-machine`](/docs/sdk-reference/statemachine-api/typescript-package), which
the frontend installs from npm. Its header says which import it was generated with and how to change it. The
package and the tool need not be at the same version: every release bumps both, mostly for changes that leave the
IR alone. What has to match is the **IR format**, the contract between the IR and the engine that reads it, which
changes only when that contract does. A twin generated with specifier imports records the format in its IR, and the
installed package checks it when the twin is imported: a format it cannot read throws on load, saying whether to
upgrade the package or regenerate, rather than run a machine it would read differently. An engine released before
the check ignores the field, and the twin imports nothing it lacks. A twin generated with relative imports reaches
into the engine checkout that generated it and records no format. After upgrading the tool, regenerate:
`trax machine check` fails on a twin left from the previous tool. What changed in `generate`'s defaults, and how
to generate as before, is in [Regenerate twins after the trax machine generate defaults changed](/docs/migration-guides/trax-machine-generate-defaults).

[`trax machine check`](/docs/reference/cli#trax-machine-check) regenerates to a temporary location and fails on any
drift. Generate and check are the same code path, so a passing check means the committed artifacts match the source.
[`trax machine new`](/docs/reference/cli#trax-machine-new-name) scaffolds a machine.

## SDK Reference

> [ExportIr](/docs/sdk-reference/statemachine-api/machine#exporting-the-ir) | [IDifferentialBuilder](/docs/sdk-reference/statemachine-api/machine#idifferentialbuilder) | [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules) | [Migrations](/docs/sdk-reference/statemachine-api/migrations) | [trax machine](/docs/reference/cli#state-machines-trax-machine)
