# Phase 0 codegen: scope and sequencing

Status: Increments A, B and E are built and shipped. Still open: Increment D (the oracle flip), retiring
the derived `structure.json` golden, and `trax machine migrate`, which is registered but unimplemented. Last
reconciled against the code 2026-09-11. §0 below is the original handoff, kept as a record of why the
architecture diverged from the plan, and annotated where it has been overtaken. Companion to
[tier1-machine-codegen.md](tier1-machine-codegen.md); this turns that RFC's §3/§6 (author in C#, export a
neutral IR, generate the rest) into a concrete, code-grounded plan.

**Update (2026-08-13).** Two things landed since. (1) The differential seeds are now authored in C# too:
`.Differential(...)` on the machine builder records samples/seeds/probe contexts, `IrExporter` emits them as
an IR `differential` block, and the harness enumerates off the IR. This supersedes part of Decision A (§6) —
`machine.json` is no longer even the differential's home. (2) nwyc's **write-to-congress** adopted the whole
pipeline as the first *product* consumer (declarative machine, generated twin, C#-authored differential,
`AddStateMachines` on Trax.Effect 1.49.0), and its `machine.json` is **deleted**. The concrete frictions nwyc
surfaced were folded into Increment E (§5).

**Update (2026-09-11). Increment E has shipped.** The `trax machine` CLI is merged in Trax.Cli
(`Commands/MachineCommand.cs`, with `Machines/{MachineGenerator,MachineLoader,NodeRunner,MachineScaffolder}.cs`
behind it), `checkout` is re-authored declaratively and generated through it, and both `machine.json` files,
`generate.mjs`, the orphaned `*.g.ts` and the codegen drift test are deleted. Two things from this plan are
still open: **Increment D** (the oracle flip) and retiring the derived `structure.json` golden. Read the plan
below with that in mind: the sections dated 2026-08-11 describe the state at the time they were written.

This scopes the one remaining Phase-0 prerequisite from the RFC, the **round-trip codegen mechanism**. In
practice that is the whole source-and-oracle inversion: a declarative C# authoring surface, the IR, the
C#->IR exporter, the generators, the round-trip attachment, and the oracle flip. JCS canonicalization (§4 of
the RFC) and the migration golden (§8) are already built.

## 0. Implementation status (handoff, 2026-08-11), historical

This is the Increment A/B handoff from 2026-08-11, kept because it records why the built architecture diverged
from the plan. It is **not current state**: everything it calls uncommitted has since been committed and merged
(the Increment A and B artifacts are `3add783` and `66254bb` and the commits around them), and Increment E has
shipped since. Read it against the 2026-09-11 update above.

At the time of writing, all tests passed: **C# state-machine suite 178, TypeScript suite 110**, zero warnings,
csharpier + prettier clean, `tsc --noEmit` clean.

### The architecture that emerged (differs from the plan in one big way)

The plan assumed each runtime would generate a hand-binding twin. Instead the built design **interprets the IR
at runtime**: guards, reducers, and context validators are DATA (declarative rules), and a small evaluator
per language runs them. Consequences:

- There is no per-machine runtime to generate for the declarative parts; the engine is untouched (rules are
  compiled to the existing delegate types in C#, and to closures over the interpreter in TS).
- **Increment C (a "C# generator target") is subsumed / not needed**: C# is the source and runs the rules
  directly via `RuleEvaluator`. There is no second C# artifact to generate.
- The generators only need to emit **types** (context/input/spec) and a thin **typed factory**, not logic.

### What is built (Increment A: C# authoring -> IR)

In `Trax.Effect/src/Trax.Effect.StateMachine/Rules/`:
- `Rule.cs` + `RuleEvaluator.cs` (guard/validator vocabulary as data + a total evaluator).
- `Reduction.cs` + `ReductionEvaluator.cs` (reducer vocabulary + non-mutating evaluator).
- `MemberPath.cs`, `ContextSchema.cs`, `SchemaReflection.cs`, `SchemaValidator.cs` (typed layer: member
  expression -> JSON field name; a C# context record + `[MinLength]` attributes -> `ContextSchema`;
  schema-derived validation with `additionalProperties: false`).
- `Rules.cs` (the ergonomic string-free helpers: `Input((CoinInput i) => i.Coin).IsOneOf(...)`, `Set(...)`,
  `Field(...)`, `All(...)`, `Clear()`, etc.).
- `DeclarativeModel.cs`, `IrExporter.cs` (the exported model + `IrExporter.Export(machine)` -> canonical IR).
- `Fluent.cs` gained `.Context<T>()` / `.Context()`, `.When(Rule)`, `.Reduce(Reduction)`, `.WithInput<T>()`
  overloads that store the declarative data on `BuiltMachine.Declarative` AND compile to the existing engine
  delegates. `CanonicalJson.cs` gained a public `Serialize(JsonNode)` for the IR.
- Proof: `tests/.../Fakes/DeclarativeTurnstile.cs` (authored with the declarative surface) reproduces the
  turnstile differential corpus byte-for-byte and matches a byte-exact IR golden
  (`tests/.../UnitTests/IrExporterTests.cs`). `RichExportMachine.cs` exercises every rule/reducer kind for
  exporter coverage.

### What is built (Increment B: IR -> running + typed TypeScript)

In `Trax.Api.StateMachine/src/rules/`:
- `interpreter.ts` (the TS mirror of the C# evaluators; 27 cases, recounted 2026-09-11, mirror the C# suite
  case-for-case).
- `irMachine.ts` (`machineFromIr(ir)` builds a runnable `MachineDefinition` from the IR).
- `generateTypes.ts` (`generateContextTypes` -> per-state/-trigger `type` aliases + a `Spec`;
  `generateMachineFactory` -> a self-contained typed factory that embeds the IR).
- Committed generated artifacts: `machines/turnstile/turnstile.ir.json` (the IR; C# would export this),
  `src/machines/turnstile/turnstile.contexts.g.ts` (types), `src/machines/turnstile/turnstile.machine.g.ts`
  (the typed `turnstile` machine). All three are drift-checked and typechecked.
- **The sample turnstile twin is migrated**: `src/machines/turnstile/turnstile.ts` is now a ~10-line
  re-export of the generated modules; all hand-written guards/reducers/validators were deleted, and the
  differential/roundtrip/migration/typed tests still pass.
- Proof: `irMachine.test.ts` shows the IR-driven TS machine reproduces the differential corpus, so
  **IR-driven TS == hand-written twin == C#**.

### What remained at the handoff, and where it stands now

Ordered by priority as it was written then (E before D; see §5 and Decision D):
- **Increment E (the `trax machine` CLI + checkout). SHIPPED (2026-09-11).** Real
  `new`/`generate`/`check` commands replaced `regen-state-machine.sh`, running the exporter and
  generators directly with per-artifact output roots, an arbitrary engine `src` and an import-style option; and
  `checkout` was re-authored declaratively (multi-key context, count guards, an effect) through the whole
  pipeline. Detailed in §5.
- **Increment D (oracle flip to C#). STILL OPEN.** Port `enumerate()` (the BFS corpus generator in
  `src/differential.ts`) to C#, so C# produces the golden and every runtime replays it. The engines are already
  proven-equivalent, so this is about which side owns the golden, not correctness. Oracle-internal; nobody is
  blocked on it. This is the one increment left.
- **Cleanup. MOSTLY DONE, one item left.** `machine.json` (both machines), `generate.mjs`, the orphaned
  `turnstile.g.ts` / `checkout.g.ts` and `src/codegen-drift.test.ts` are deleted, and Decision A is settled in
  the IR's favor. `structure.json` was **not** retired: it is still committed per machine and still
  drift-checked against `describe()` (`src/fixtures.ts`, and the two `roundtrip.test.ts` suites). Retiring it,
  or regenerating it from the IR, is the only piece of this cleanup still outstanding.

### Handoff gotchas

- **Nothing is committed** *(done since: the work was branched per repo with `./branch-and-push.sh` across
  `Trax.Effect`, `Trax.Api.StateMachine`, and, for the rename, `Trax.Samples` + `Trax.Docs`, and merged)*.
- **The sample needs a re-pack.** `Trax.Samples` consumes `Trax.Effect` by package; after C# changes run
  `./pack-local.sh` then `rm -rf ~/.nuget/packages/trax.*/1.99.99`. Revert any pack-induced
  `packages.lock.json` changes before committing (they are pollution, deps did not change).
- **Schema validation is stricter than the old twins:** `SchemaValidator` enforces `additionalProperties:
  false` (matching `machine.json`'s intent and the C# side). A v2 that adds a field needs its own validator
  (see how `migration.test.ts` overrides the v2 `Unlocked` validator).
- **Run the suites:** C# `dotnet test Trax.Effect/tests/Trax.Effect.StateMachine.Tests/...`; TS
  `cd Trax.Api.StateMachine && npx vitest run` and `npx tsc --noEmit`.

## 1. The baseline this plan started from (grounded, 2026-08-11)

Everything this section describes in the present tense is gone: `machine.json`, `tools/state-machine-codegen/
generate.mjs` and the hand-written twins were all deleted by Increment E. It is kept because the asymmetry it
names is the gap the whole plan exists to close.

The two runtimes sat at opposite ends of "how much is data", and that asymmetry is the whole story.

**The TypeScript side is already IR-shaped.** `machines/<m>/machine.json` is a hand-authored spec carrying:
identity, structure (states/triggers/transitions), guard/reduce **bound by name** (`"guard": "acceptedCoin"`),
a per-state **context schema** as JSON-Schema fragments (turnstile has it; checkout does **not**), and
differential samples/seeds. `tools/state-machine-codegen/generate.mjs` reads it and emits `*.g.ts` (state/
trigger unions + an edge table with guard/reduce as string names) and proves `structure.json` is derivable.
The twin (`turnstile.ts`) then binds names to hand-written functions from a `guards`/`reducers` registry and
supplies per-state `contextValidators`. So the TS side already has the name-binding model the IR needs.

**The C# side is the opposite.** Guards, reducers, context validators, and migrations are all anonymous
inline `Func<>` delegates on `MachineDefinition`/`TransitionDefinition` (`Fluent.cs`): guard is
`Func<JsonObject, JsonNode?, bool>`, validator is `Func<JsonObject, string?>`, reducer is
`Func<JsonObject, JsonNode?, JsonObject>`. `SnapshotMachine.Describe()` emits **structure only** (id, version,
initialState, states, triggers, transitions as from/trigger/to), deliberately excluding behavior. There are
no handler **names**, no context **schema as data**, no validator/guard/effect/committed/migration metadata in
the export. Even the effect binding is a runtime `Type`, not a declarative name.

The consequence, stated plainly: **the IR's entire declarative layer cannot be exported from C# today, because
in C# it does not exist as data at all.** This is the long pole, and it is a C#-authoring problem, not a
generator problem.

## 2. The core gap

To export an IR, three things must become **data** on the C# side (they are already data-ish on the TS side):

1. **A per-state context schema** (field names, JSON types, nullability, enum domains). Today it lives only
   inside validator closures like `ItemsIsArray(ctx) && ReceiptEmpty(ctx) && TotalIsNumber(ctx)`.
2. **A declarative guard/validator vocabulary** (composable, inspectable rule objects) for the common cases,
   plus **named** custom handlers for the rest. Today every guard/validator is an anonymous closure with no
   name and no inspectable structure.
3. **Named custom-handler bindings** so a generator can emit "this edge needs a `promoValid` guard you must
   implement", and a missing one is a compile error, not a silent no-op.

Everything else the IR needs is already extractable: id/version (data), state/trigger enums (reflection),
edge topology (`TransitionDefinition`), committed states (`BuiltMachine.CommittedStates`, just not in
`Describe()` yet), and effect bindings (as `EffectType.FullName` + key prefix).

## 3. The declarative vocabulary is small (sized empirically)

A survey of every guard/validator/reducer across turnstile + checkout (both runtimes, the C# fakes, and the
sample) found ~26 instances. A vocabulary of **6-10 primitives covers ~92%**:

| Primitive | Kind | Covers |
|---|---|---|
| `field-type(path, string\|number\|array)` | guard/validator | type checks |
| `field-non-empty-string(path)` | guard/validator | non-empty strings |
| `field-nullish(path)` / `field-present(path)` | guard/validator | presence/absence |
| `array-count(path, op, n)` | guard/validator | `items.length > 0` |
| `numeric-compare(path, op, n)` | guard/validator | `total > 0` |
| `set-member(input-path, set)` | guard | accepted-coins |
| `and(...preds)` | combinator | multi-clause guards |
| `assign-constant(literal)` | reducer | resets / fresh context |
| `copy-input-field(from -> to)` | reducer | record an input field |
| `clone-merge(field <- source)` | reducer | keep context, set one field |

The genuinely-custom ~8% (name it, hand-write it): a **formula reducer** (`total = itemCount * price`), a
couple of **guard-clause variations** across implementations, and per-language **extraction helpers** (inline
these into generated bodies; do not make them primitives). Migrations stay hand-written named lambdas by
design (schema evolution is not vocabulary-shaped).

The risk to manage (RFC §2): keep the vocabulary *small*. Let it grow to cover everything and it becomes the
serialized-logic DSL the whole approach exists to avoid.

## 4. Components (assessed 2026-08-11)

Six pieces. For each: what existed then, what was new, and the hard part. `machine.json` and `generate.mjs`,
named below as what exists, are deleted now. Increments A, B and E built pieces 1, 2, 3 and 5; piece 4's C#
generator target was subsumed rather than built, because the IR is interpreted at run time (see below); and
piece 6 is Increment D.

1. **The IR (schema + version).** *Exists:* `machine.json` is an informal, IR-shaped spec that already drives
   TS. *New:* a versioned JSON Schema formalizing it, plus the declarative layer (context schema for every
   state, declarative validators/guards, named custom-handler bindings, effect bindings, committed states,
   migration metadata). *Hard part:* deciding whether the IR **is** a formalized `machine.json` (keep and
   extend) or a fresh artifact (see Decision A). Its evolution rules are already scoped in RFC §5.

2. **The declarative authoring surface in C# (the long pole).** *Exists:* nothing; guards/validators are
   closures. *New:* a context-schema builder and the vocabulary from §3 as composable data objects, plus a
   `.When(named(...))` / `.Holds(named(...))` path that records a **name** alongside the rule. *Hard part:*
   API ergonomics. It has to be as pleasant to author as today's lambdas or nobody will use it, and it must
   degrade cleanly to a named hand-written handler for the ~8%.

3. **The C#->IR exporter.** *Exists:* `Describe()` emits the structural third. *New:* extend the built model
   and the exporter to emit the declarative layer + committed states + effect + migration metadata. *Hard
   part:* only possible **after** piece 2, because you cannot export what is not data. The structural exporter
   is the easy 20% and already done.

4. **The generators (IR -> code).** *Exists:* `generate.mjs` emits TS structure + a `--check` drift gate.
   *New:* generalize it to consume the IR and emit, per language: the context type, the shape + declarative
   validators (TS can use valibot, already a dependency), custom-handler interface stubs, and a differential
   replay entry; then add a **C# generator target** (the `csOut` the generator of the day left a stub for).
   *Hard part:* the C# target is net-new; and validators-from-schema is new on both sides. **Not built, and
   will not be:** C# is the source and interprets the IR directly, so there is nothing to generate for it.

5. **The round-trip attachment.** *Exists:* the TS name-binding registry (`guards[name]`) is a working
   precedent; C# has no equivalent (anonymous). *New:* the RFC §6 contract, generated interface of named
   handlers, human implements in a partial (C#) or a separate hand-owned file (TS), missing = compile error.
   *Hard part:* making "add a field, regenerate, watch the build break" clean in both languages.

6. **The oracle flip.** *Exists:* TS `enumerate()` is the oracle; C# `DifferentialCorpus.Replay` already
   exists (so the flip is smaller than it looks, RFC §4). *New:* a C# enumerator that produces the golden.
   *Hard part:* porting `enumerate()`'s BFS + seed + dense-context traversal and stable ordering to C#. Can
   be deferred; keep TS as oracle through the early increments.

## 5. Sequencing (de-risk the long pole first)

The order is chosen so each increment is independently verifiable and the riskiest unknown (piece 2) is
proven before anything depends on it.

- **Increment A: the declarative surface + context schema in C#, exported.** Build piece 2's context-schema
  builder and the §3 vocabulary as data, re-author **turnstile** (the simplest machine) with them, and extend
  the exporter (piece 3) to emit an IR that includes the context schema + declarative guards + named
  bindings. *Verify:* the C#-exported IR for turnstile matches a committed IR golden, and that golden's
  structure/context content matches the existing `machine.json` (parity between the new C# source and today's
  spec). No generator or oracle change yet. This proves the core "C# can express the declarative layer as
  data" question end to end on one machine.

- **Increment B: generate TS from the IR (piece 4, TS target).** Point `generate.mjs` at the IR (not the
  hand-authored `machine.json`) and add validator + context-type + handler-stub emission. *Verify:* the
  generated `*.g.ts` still satisfies the existing TS suite and the drift gate, and the differential stays
  green (TS still the oracle). This proves the IR drives the existing pipeline.

- **Increment C: the C# generator target + round-trip (pieces 4 C#, 5). SUBSUMED (2026-08-11).** The built
  design interprets the IR at runtime (see §0), so there is no second C# artifact to generate: C# is the
  source and runs the declarative rules directly via `RuleEvaluator`. The handler-attachment round-trip only
  applies to a genuinely-custom named handler, and no machine has one yet, so it is designed but unexercised.
  This increment is folded into E (the CLI emits the frontend twin; the C# side needs no generator).

- **Increment D: the oracle flip (piece 6).** Move the enumerator to C#; both runtimes replay the C#-produced
  golden. *Verify:* the corpus is byte-identical to the prior TS-produced golden for the existing machines
  (no behavior change), then C# is the source of truth.

- **Increment E: the `trax machine` CLI + checkout end to end (RFC §9). Scheduled *before* Increment D**
  (the oracle flip), because the CLI removes friction a real team hits on every machine edit today, while the
  flip is an internal correctness move nobody is blocked on. Prove the richer `checkout` machine through the
  whole pipeline and replace `regen-state-machine.sh` with real commands. Detailed below.

Increments A and B keep TS as the oracle, so early work does not destabilize the differential. E is now
sequenced ahead of D; the flip (D) lands once the CLI and checkout are solid, and it does not block E because
E is generator/authoring work that is oracle-agnostic.

### Increment E in detail: the `trax machine` CLI (SHIPPED 2026-09-11)

This is the plan the CLI was built from, updated to the surface that actually shipped.

**What it replaced.** nwyc regenerated every derived artifact with `scripts/regen-state-machine.sh` (invoked
as `pnpm regen:state-machine`), a three-step chain of env-var-gated golden tests run in strict order:

```
1/3  IR      C# machine → *.ir.json           UPDATE_IR=1 dotnet test …~IrTests
2/3  twin    *.ir.json → *.contexts.g.ts +    UPDATE_GEN=1 npx jest …'\.codegen\.test\.'
             *.machine.g.ts
3/3  corpus  *.ir.json → differential.json     UPDATE_GOLDEN=1 npx jest …'\.differential\.test\.'
```

Each step is the *same* golden test that guards the artifact in CI, run with its update flag set, so the
generator and the drift check cannot disagree. It works, but it is fragile for reasons that are the CLI's
requirements, not incidental:

| Friction in the script | Requirement on `trax machine` |
|---|---|
| Artifacts are split across two trees: the IR + corpus under a shared `machines/<name>/` dir, the twin (`*.contexts.g.ts` + `*.machine.g.ts`) next to the frontend that consumes it (`apps/web/src/app/<name>/`). | `generate` / `check` take an **output root per artifact** (`--ir-out`, `--twin-out`, `--corpus-out`), not one directory. |
| The consumer **vendors** the engine `src` (pinned in `VENDORED.md`) and imports it via a `@trax/state-machine` path alias, rather than depending on a published package. | The twin generator runs against an **arbitrary engine `src`** (`--engine-src <path>`); it must not hard-wire a package layout. |
| `generateMachineFactory` emits imports relative to the engine's own `src` (`../../machine`, `../../rules/irMachine`, `../../typed`). nwyc's codegen test collapses the three into one `@trax/state-machine` import with three hard-coded string `.replace()` calls (two deletions + one rewrite) because the generator has no option for it. | The twin generator takes an **import style** (`--import-style relative|specifier`, with the specifier configurable), so no consumer re-implements the rewrite. This is a change to `generateMachineFactory`'s signature (add an options arg), currently `(ir: IrDocument)`. |
| Generation is driven through a test runner. Under nx, `nx test web --testPathPattern` does not filter, so it runs the whole web suite and one unrelated failing test aborts the regen under `set -e`. The script works around this by calling `npx jest` directly. | `generate` runs the exporter and generators **directly**, never through a test runner. This is the substantive argument for a CLI over "wrap the tests in a better script." |

**Command surface** (mirrors the existing `trax` generator architecture, `GenerateCommand` / a code renderer):

- `trax machine new <name>` — scaffold the C# source skeleton (states/triggers/context + a couple of
  transitions), the effect stub if `--with-effect`, and the differential wiring. Consistency by construction.
- `trax machine generate --assembly <file> [--machine <name>] [--ir-out …] [--twin-out …] [--corpus-out …]
  [--engine-src …] [--import-style …] [--specifier …] [--tools-dir …] [--node …]`: reflect the machine out
  of an already-compiled assembly, `ExportIr()` it, then emit the IR, the twin (`generateContextTypes` +
  `generateMachineFactory`) and the corpus, atomically (write to a staging dir and swap; §RFC 6). Idempotent: a
  second run with no source change produces no diff. `--assembly` is required; `--machine` is needed only when
  the assembly holds more than one machine. The corpus does **not** wait for D: pre-D it comes from the
  TypeScript `enumerate` through `tools/generate-corpus.mjs` (see the execution model below).
- `trax machine check <…same roots…>` — regenerate to a temp location and diff against the committed
  artifacts; non-zero exit on drift. This is the CI gate, and it is the *same* code path as `generate`, so
  they cannot disagree (the property the golden-test chain gets today by construction).
- `trax machine migrate` — schema-diff + forward-migration scaffold. Stubbed until migrations are carried in
  the IR (Decision E; `MigrateFrom` already exists at the engine level); the command exists so the surface is
  complete.

**Execution model: how the C# tool drives the TypeScript generators.** This is the one thing the CLI must
decide before anything else, because after the interpret-at-runtime pivot there is no C# generator: the IR
export is C# (`IrExporter.Export`), but the twin generators (`generateContextTypes` / `generateMachineFactory`)
and the pre-D corpus enumerator (`enumerate`) are TypeScript, in the engine's `src/rules/`. So `generate` is a
C# **orchestrator** that crosses the runtime boundary explicitly:

- **IR, in-process C#.** The CLI references `Trax.Effect.StateMachine.Persistence`, loads the consumer's
  already-compiled assembly, reflects the machine out of it (`MachineLoader`), calls `IMachine.ExportIr()`
  (which is `IrExporter.Export` on the built machine), and writes `<machine>.ir.json` to `--ir-out`. It
  compiles nothing, and no Node is involved.
- **Twin (and pre-D corpus), shell out to Node.** The CLI spawns `node` against a thin entrypoint shipped in
  the engine repo (`tools/generate-twin.mjs`) that bundles the TS generators from `--engine-src` and writes the
  `.g.ts` files to `--twin-out` for the given `--ir` and `--import-style`; `tools/generate-corpus.mjs` does the
  same with `enumerate` / `serializeCorpus` for `--corpus-out`. This *is* the "run the generators directly, not
  through a test runner" requirement: it replaced nwyc's `npx jest … .codegen.test` invocation with a direct
  `node tools/generate-twin.mjs …`, and it removed the `.replace()` import hack because the entrypoint passes
  `--import-style` into `generateMachineFactory`'s options arg. **Delivered:** the two `tools/*.mjs`
  entrypoints and the shared `tools/lib/`, plus the options arg on `generateMachineFactory`.
- **Consequence.** The `dotnet tool` needs `node` on `PATH` for the twin/corpus steps (the IR-only path and a
  `check` of the IR do not) **and an engine checkout whose `node_modules` are installed**. The entrypoints
  bundle the engine `src` with esbuild (`tools/lib/engine-loader.mjs`) so the CLI runs the exact source the
  consumer ships rather than a possibly-stale `dist`, and esbuild is not a direct dependency of the engine
  package: it arrives transitively through `tsup`/`vitest`. A missing esbuild throws with that instruction
  rather than degrading silently. Acceptable: the artifacts are produced by Node anyway, and Trax.Cli already
  ships a `package.json`, so Node-in-the-repo has precedent.
- **Alternative (fallback, not taken).** Keep twin generation entirely in the consumer's Node toolchain (a
  `package.json` script running the same `tools/generate-twin.mjs`) and let the C# CLI own only the IR export
  and `check`. This drops the Node dependency from the dotnet tool but splits the workflow across two entry
  points. **Decided (2026-09-11): the single-orchestrator model shipped** and the fallback was not needed; the
  Node dependency has cost only the two requirements in the consequence above.

**Scope of the first pass.** Build and test the CLI in this workspace against `turnstile` and `checkout` (the
personal-repo machines); their committed IR/twin/corpus are the byte-parity oracle. write-to-congress lives in
the nwyc work repo, so validate against it separately (point the CLI at nwyc's vendored engine `src` and its
committed artifacts via `--engine-src`) rather than depending on nwyc from here. And note that Increment E
deliberately bundles two real sub-projects, not incidental to the CLI: the `checkout` declarative migration
(test item 6) and the `machine.json` / `generate.mjs` retirement (cleanup below). Both landed.

**Oracle-agnostic by design.** Pre-D the corpus comes from the TypeScript `enumerate()` (the
`generate-corpus.mjs` step above); post-D the C# enumerator produces it in-process and `generate` owns all
three artifacts natively. Either way the IR export and the drift check are unchanged, so E does not wait on D.

**Test strategy (test-first, per the repo rules).** The regen script's three golden tests already *are* the
oracle for what the CLI must reproduce, so the CLI is correct iff its output is byte-identical to theirs:
1. **Byte-parity with the existing pipeline.** `trax machine generate` on `turnstile` (then `checkout`)
   produces the exact committed `*.ir.json`, `*.contexts.g.ts`, `*.machine.g.ts` and `differential.json` the
   current pipeline produces. Assert byte-equality against the checked-in artifacts.
   Then validate cross-repo: run it against nwyc's write-to-congress (`--engine-src` its vendored engine) and
   assert byte-parity with what `regen-state-machine.sh` produces there.
2. **Idempotence.** A second `generate` with no source change yields no diff; `check` exits zero.
3. **Drift detection.** Mutate the C# source (add a state), run `check`, assert non-zero exit and a message
   naming the drifted artifact. Revert.
4. **Import style.** `--import-style specifier` emits the single `@trax/state-machine` import; `relative`
   emits the three `../../…` imports. Both typecheck in their respective layouts.
5. **Split roots.** `generate` writes each artifact to its own `--*-out` root and touches nothing else.
6. **`checkout` end to end.** Re-author `checkout` declaratively (multi-key context, count guards, the
   exactly-once effect), `generate` its artifacts, and confirm its differential is green — the second machine
   is where the generator's real requirements surface.

**Cleanup folded into E (done, except one item).** `machine.json` (both machines), `generate.mjs`, the
orphaned `turnstile.g.ts` / `checkout.g.ts` and the codegen drift test are deleted, so the IR is the sole spec
everywhere. `structure.json` was **not** retired: it is still committed per machine and still drift-checked
against `describe()`, and retiring it (or regenerating it from the IR) is the one piece left.

## 6. Decisions this scope forces

- **Decision A: machine.json's fate. LOCKED (2026-08-11): the IR is the formalized `machine.json`.** It is
  versioned and extended with the declarative layer; the C# machine is the authoring source and *exports* this
  IR, and the generator keeps consuming it. We do **not** retire `machine.json` (the RFC's alternative); it
  becomes the generated IR artifact instead of a hand-authored one. Rationale: the generator and differential
  already read it, so this is the minimal-churn path, and it keeps one artifact as the interchange contract
  rather than introducing a second. Concretely, the pipeline becomes: C# machine (source) -> `machine.json`
  (IR, generated) -> `generate.mjs` (unchanged consumer) -> per-language code.
  **Superseded (2026-08-13, completed 2026-09-11):** the differential seeds moved into the C# source
  (`.Differential(...)` -> IR `differential` block), so `machine.json`'s last unique role was gone, and
  Increment E then deleted the file and `generate.mjs` for every machine. The "keep `machine.json` as the
  generated IR artifact" framing above is dead. The pipeline as built is: C# machine (source) ->
  `<id>.ir.json` (IR, exported) -> `generateContextTypes` / `generateMachineFactory` -> the typed twin.
- **Decision B: the vocabulary boundary.** Recommended: ship the ~8 primitives in §3, everything else is a
  named hand-written handler, and migrations stay named lambdas. Revisit only with a real third machine
  (RFC's Phase 1), not speculatively.
- **Decision C: round-trip attachment shape. SUBSUMED for declarative machines (2026-08-11).** Because the IR
  is interpreted at runtime (§0), a declarative machine has no hand-written logic to attach: the twin is a
  generated `*.machine.g.ts` plus a hand-owned *bridge* file with UI types and no guards/reducers (see
  write-to-congress). The attachment shape (C# `partial` + generated interface; TS interface + registry +
  separate hand-file) still stands for the first *custom* named handler, which is unexercised; confirm it then.
- **Decision D: oracle flip timing. Now scheduled *after* Increment E.** Keep TS as the oracle through A, B,
  and E (the CLI + checkout). The flip is oracle-internal correctness that nobody is blocked on; the CLI is
  friction a real team hits daily, so it goes first. Do not flip early: it multiplies moving parts during the
  authoring/generator work, and E is oracle-agnostic so it does not need the flip.
- **Decision F: deploy topology. LOCKED (2026-08-13): vendor the engine, check the artifacts in.** The
  consuming repo mirrors the TypeScript engine `src` at a pinned upstream commit (recorded in `VENDORED.md`)
  and imports it through a path alias (`@trax/state-machine`), and commits the generated twin, the IR, and the
  differential corpus into its own tree (twin next to the frontend, IR + corpus in a shared `machines/<name>/`
  dir). The C# side consumes Trax by exact-pinned NuGet (`Trax.Effect.StateMachine`). Rationale: the engine and
  machines co-evolve tightly, the twin must build against an arbitrary engine `src` (not a fixed package
  layout), and checking the corpus into the consumer's tree is what makes the differential run in *its* CI
  (closing the upstream skip-on-missing hole, RFC §7). Cost: a manual re-vendor step (bump the pin, re-copy
  `src`, regenerate), which `trax machine generate` should reduce to one command. This closes the RFC's
  deploy-topology open question in favor of vendoring over a published npm package.
- **Decision E: versioning and migration. DEFERRED (2026-08-11).** Machines are effectively single-version
  for now. A stored snapshot whose version does not match the current definition is *rejected* as a typed
  `version-mismatch` (the engine's existing behavior when no migration is registered) and the client starts
  fresh. What is deferred is carrying migrations into the IR and the generated frontend: no migration section
  in the IR, and the declarative machines stay single-version. The engine-level `MigrateFrom` authoring API
  (a delegate forward-migration on the fluent builder, applied on `Rehydrate`) and its golden already exist
  and stay green, so this is a free simplification: the reject-old-drafts posture needs no new code, and IR
  migrations are additive, addable later without restructuring. The `turnstile` v2 migration golden
  (`machines/turnstile/migration.json`, `TestTurnstileV2`) stays green; deferral means not building *more*
  migration machinery, not removing what exists. Accepted cost: a schema
  change invalidates in-flight drafts (fine for a POC, revisited before a product with valuable persisted
  drafts).

## 7. Recommended first build: Increment A (built)

Smallest verifiable slice that de-risks the whole effort. Concretely:

- A `Trax.Effect.StateMachine` **context-schema** type set (per-state field list: name, JSON type,
  nullable, optional enum) and a **rule** type set for the §3 vocabulary, both plain data.
- Fluent surface: `.Holds(...)` / `.When(...)` overloads that accept a rule (or a `named("promoValid")`
  custom handler) and record it as data on the built model, alongside the existing delegate path (so nothing
  breaks during migration).
- An `IR` exporter (extend `Describe()` or a sibling `DescribeIr()`) that emits identity + structure +
  per-state context schema + declarative guards/validators + named custom-handler bindings + committed states
  + effect bindings.
- Re-author **turnstile** in C# using the declarative surface.

**Test strategy (test-first, per the repo rules):**
1. Round-trip: the C#-exported IR for turnstile equals a committed `turnstile.ir.json` golden (a new
   fixture), asserted byte-exact through the canonical writer.
2. Parity: the golden's structure + context-schema content matches the existing `machine.json` context block
   (the new C# source agrees with today's spec).
3. Vocabulary unit tests: each primitive evaluates correctly (a `field-non-empty-string` rule accepts
   `"x"`, rejects `""` and a number), so the generated validators have a tested semantics to match.
4. No-regression: the existing turnstile engine/differential/serialization suites stay green (the declarative
   re-author must not change behavior).

Increment A touches only `Trax.Effect.StateMachine` (+ its tests) and adds one IR golden fixture. It does not
require the generator, the C# target, or the oracle flip, so it is a clean, self-contained first PR that
answers the one question everything else depends on: can C# author and export the declarative layer as data.

## 8. The authoring API (extremely straightforward)

The design goal: **each common case is one short, English-reading line, no more ceremony than today's
lambda, but it produces data instead of an opaque closure.** Three author-facing surfaces, plus a named
escape hatch for the ~8%:

- `.Context(...)` declares a state's context schema (field name, JSON type, nullability, constraints).
- `.When(...)` declares a guard from the vocabulary.
- `.Reduce(...)` declares a reducer from the vocabulary.
- A **named lambda** (`.When("promoValid", (ctx, input) => ...)`) is the only escape for genuinely-custom
  logic; the name is what the generator emits as a required handler in every runtime.

The vocabulary is a set of static factory helpers, imported with `using static` so the author writes them
bare. This section is the design sketch that preceded Increment A; the shipped helpers are in
`Trax.Effect.StateMachine/Rules/Rules.cs` and read slightly differently (`Input((CoinInput i) => i.Coin)
.IsOneOf(...)`, and the machine id is declared with `m.Id("turnstile")` rather than derived from the class
name), but the shape below is what landed:

| Surface | Helpers |
|---|---|
| Context field | `Field("f").String()` / `.Number()` / `.Array()` / `.Bool()`, then `.Nullable()`, `.NotEmpty()`, `.OneOf(...)`, `.Min(n)`, `.Max(n)` |
| Guard | `Field("f")` or `Input("f")`, then `.Present()`, `.Absent()`, `.NotEmpty()`, `.OneOf(...)`, `.GreaterThan(n)`, `.Equals(v)`; `Count("arr").GreaterThan(n)`; `All(...)`, `Any(...)` |
| Reducer | `Set("f", FromInput("x"))`, `Set("f", Value(v))`, `Reset()` (initial context), `Clear()` (empty), default = carry forward |

**Strongly typed, no structural magic strings.** Context and input are C# records; the schema and its
constraints fall out of the type, and every field is referenced by a member expression, never a string. The
only strings left are genuine text (`Because(...)`) or domain values (`"quarter"`). Turnstile, authored this
way:

```csharp
// Context and input as records. The schema + constraints ARE the type; no field-name strings anywhere.
public sealed record UnlockedContext
{
    [Required, MinLength(1)] public string PaidWith { get; init; } = "";
}
public sealed record CoinInput { public string Coin { get; init; } = ""; }

protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
{
    m.Id("turnstile").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

    m.In(TurnstileState.Locked).Context()                       // empty context
        .On(TurnstileTrigger.Coin).WithInput<CoinInput>()
            .When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))   // i.Coin, not "coin"
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput(i => i.Coin))
            .To(TurnstileState.Unlocked);

    m.In(TurnstileState.Unlocked).Context<UnlockedContext>()    // schema comes from the record
        .On(TurnstileTrigger.Push)
            .Reduce(Clear())
            .To(TurnstileState.Locked);
}
```

The member expressions (`i => i.Coin`, `u => u.PaidWith`) are `Expression<>` trees: inspectable, so codegen
still extracts the field name as data, but authoring is refactor-safe with zero structural strings. Two
strings removed for free along the way: the effect key (`RunsOnce<TEffect>()` derives it from the machine id
+ trigger) and named-handler names (a `nameof` / method reference). The machine id stayed an explicit
`m.Id(...)` rather than being derived from the class name.
The state-machine `IEffect` is renamed `ISnapshotEffect` to stop colliding, in a reader's head, with the core
Trax.Effect abstractions.

Two things that keep it straightforward:

- **The initial context was to be derived from the initial state's schema** (`Array()` -> `[]`, nullable ->
  `null`, `Number()` -> `0`), so that `StartsAt(state)` would need no factory in the common case. That part
  was not built: the shipped `StartsAt` takes the state and a context factory, and every machine supplies
  one.
- **The rules run through a shared interpreter, so behavior is identical to the lambda version.** A guard
  rule is data; the engine evaluates it. This is the key to Increment A: turnstile re-authored this way must
  produce a byte-identical differential corpus, which is exactly what the existing suite proves. Codegen (a
  later increment) emits per-language validators from the *same* rule data (TS via valibot); until then the
  interpreter is the single implementation both the export and the runtime share.

Custom logic is named, never anonymous: `.When("payable", (ctx, input) => ...)`. The name is the
cross-language contract; the body is hand-written per runtime and differential-guarded. With the vocabulary
covering ~92% and migration deferred (Decision E), most machines have zero named custom handlers.

### What the author writes vs what is generated

Per machine, the author writes **one C# file** and nothing else machine-shaped:

| Author writes (C#, per machine) | Notes |
|---|---|
| `enum XState { ... }`, `enum XTrigger { ... }` | the states and triggers |
| `class XMachine : Machine<XState, XTrigger>` with `Configure(m)` | the whole definition lives here |
| &nbsp;&nbsp;· `m.Id("x").StartsAt(State.Initial, ctx)` | `Version()` optional (defaults to 1; rarely touched with migration deferred) |
| &nbsp;&nbsp;· `.Context(...)` per state | the declarative context schema |
| &nbsp;&nbsp;· `.In(s).On(t)...To(s)` | the transitions |
| &nbsp;&nbsp;· `.When(...)`, `.Reduce(...)` | declarative helpers, or a `named("x", lambda)` for the rare custom case |
| &nbsp;&nbsp;· `.Committed()`, `.RunsOnce<TEffect>(...)` | committed states and effect bindings |
| &nbsp;&nbsp;· `.Sample(t, input)` / `.Seed(s, ctx)` | differential probe inputs (test data; until probe-gen exists) |

Plus, only when they apply:
- **Effect** (if the machine has one): `interface IX : ISnapshotEffect` + a class implementing it (server code).
- **Named custom handler bodies per frontend language**: only the bodies of any `named(...)` guards/reducers,
  hand-written in each frontend language. Often **zero**.

One-time, not per machine:
- **Host wiring**: `AddStateMachines(assembly)`, an `ISnapshotPrincipal` (maps auth to a user key), and
  the effect DI registrations.
- **The frontend UI** that drives the machine (app code, uses the generated transport; not machine-shaped).

Everything else is **generated from that one C# file**:

| Generated (from the C# machine) | For |
|---|---|
| the IR (`<id>.ir.json`) | the interchange contract |
| state/trigger types + the transition table | every runtime |
| the context type(s) | every runtime |
| the shape validators + declarative guards | every runtime (TS via valibot) |
| interface/stubs for any `named(...)` handlers | every runtime (a missing one is a compile error) |
| the differential `replay` entry | every runtime |
| the four GraphQL mutations + effect-runner wiring | server only |
| the differential corpus (golden) | produced by the oracle enumerating the machine |

Shared infrastructure, written once and reused by every machine (neither authored per-machine nor generated):
the engine (`advance`/`rehydrate`/`serialize`/canonicalize) per language, and the persistence layer (snapshot
store, effect-claim ledger, the generic four-mutation implementation).

**The payoff:** for a machine that fits the vocabulary, the only thing hand-written per frontend is the UI
that drives it. The machine's structure, types, and validation all fall out of the single C# file.

## 9. Risks specific to this work

- **The authoring API is the adoption bet.** If the declarative surface is more painful than a lambda,
  authors route around it and the IR loses its declarative layer again. Ergonomics is a first-class
  requirement of Increment A, not a polish pass.
- **Vocabulary creep.** Every "just one more primitive" pushes toward the serialized-logic DSL. Hold the line
  at §3; the escape hatch is a named hand-written handler, not a bigger vocabulary.
- **Two big flips are coupled (RFC §3).** Source (C#) and oracle (C#) both invert. This scope deliberately
  separates them across increments so they are never in flight at once.
- **The generator becomes load-bearing.** From Increment B on, a generator bug breaks every machine. It needs
  its own tests, clear errors, and a watch mode before it is on the critical path.
