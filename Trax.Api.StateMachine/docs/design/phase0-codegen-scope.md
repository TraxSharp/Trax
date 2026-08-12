# Phase 0 codegen: scope and sequencing

Status: Implementation in progress (2026-08-11). Increments A and B are built and proven; see
**§0 Implementation status** below before reading the plan. Companion to
[tier1-machine-codegen.md](tier1-machine-codegen.md); this turns that RFC's §3/§6 (author in C#, export a
neutral IR, generate the rest) into a concrete, code-grounded plan.

This scopes the one remaining Phase-0 prerequisite from the RFC, the **round-trip codegen mechanism**. In
practice that is the whole source-and-oracle inversion: a declarative C# authoring surface, the IR, the
C#->IR exporter, the generators, the round-trip attachment, and the oracle flip. JCS canonicalization (§4 of
the RFC) and the migration golden (§8) are already built.

## 0. Implementation status (handoff, 2026-08-11)

Read this first. Nothing below is committed to git; it lives in the working trees of `Trax.Effect` and
`Trax.Api.StateMachine`. All tests pass: **C# state-machine suite 178, TypeScript suite 110**, zero warnings,
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
- `interpreter.ts` (the TS mirror of the C# evaluators; 24 tests mirror the C# suite case-for-case).
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

### What remains

- **Increment D (oracle flip to C#):** port `enumerate()` (the BFS corpus generator in `src/differential.ts`)
  to C#, so C# produces the golden and every runtime replays it. The engines are already proven-equivalent, so
  this is about which side owns the golden, not correctness.
- **Increment E (checkout + CLI):** re-author checkout declaratively (multi-key context, count guards, an
  effect), generate + migrate its twin, and add a `trax machine` command group to `Trax.Cli` that runs the
  exporter/generators (`new`/`generate`/`check`).
- **Cleanup:** the OLD pipeline still coexists. `machine.json` + `generate.mjs` + `turnstile.g.ts` +
  `structure.json` are still present and drift-checked; the migrated `turnstile.ts` no longer imports
  `turnstile.g.ts` (it is orphaned but valid). Deciding whether the IR replaces `machine.json` for real (per
  Decision A) and retiring the old node generator is outstanding. `checkout` still uses the old hand-written
  twin.

### Handoff gotchas

- **Nothing is committed.** When ready, branch per repo (`./branch-and-push.sh`) across `Trax.Effect`,
  `Trax.Api.StateMachine`, and (for the rename) `Trax.Samples` + `Trax.Docs`. Use `fix:`/`feat:` types.
- **The sample needs a re-pack.** `Trax.Samples` consumes `Trax.Effect` by package; after C# changes run
  `./pack-local.sh` then `rm -rf ~/.nuget/packages/trax.*/1.99.99`. Revert any pack-induced
  `packages.lock.json` changes before committing (they are pollution, deps did not change).
- **Schema validation is stricter than the old twins:** `SchemaValidator` enforces `additionalProperties:
  false` (matching `machine.json`'s intent and the C# side). A v2 that adds a field needs its own validator
  (see how `migration.test.ts` overrides the v2 `Unlocked` validator).
- **Run the suites:** C# `dotnet test Trax.Effect/tests/Trax.Effect.StateMachine.Tests/...`; TS
  `cd Trax.Api.StateMachine && npx vitest run` and `npx tsc --noEmit`.

## 1. Current reality (grounded)

The two runtimes sit at opposite ends of "how much is data", and that asymmetry is the whole story.

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

## 4. Components

Six pieces. For each: what exists, what is new, and the hard part.

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
   replay entry; then add a **C# generator target** (unbuilt, the `csOut` the current generator leaves a stub
   for). *Hard part:* the C# target is net-new; and validators-from-schema is new on both sides.

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

- **Increment C: the C# generator target + round-trip (pieces 4 C#, 5).** Emit C# from the IR and wire the
  handler-attachment so a missing custom handler fails the build. *Verify:* the worked loop from RFC §6 (add a
  field in the C# source, regenerate, both builds break at the exact missing handler, implement, differential
  green).

- **Increment D: the oracle flip (piece 6).** Move the enumerator to C#; both runtimes replay the C#-produced
  golden. *Verify:* the corpus is byte-identical to the prior TS-produced golden for the existing machines
  (no behavior change), then C# is the source of truth.

- **Increment E: checkout end to end + the `trax machine` CLI (RFC §9).** Prove the second, richer machine
  through the whole pipeline and scaffold the `trax machine new/generate/check/migrate` commands driven by
  what A-D actually needed.

Increments A and B keep TS as the oracle, so early work does not also destabilize the differential. The flip
(D) lands only once the C# authoring + IR export are solid.

## 6. Decisions this scope forces

- **Decision A: machine.json's fate. LOCKED (2026-08-11): the IR is the formalized `machine.json`.** It is
  versioned and extended with the declarative layer; the C# machine is the authoring source and *exports* this
  IR, and the generator keeps consuming it. We do **not** retire `machine.json` (the RFC's alternative); it
  becomes the generated IR artifact instead of a hand-authored one. Rationale: the generator and differential
  already read it, so this is the minimal-churn path, and it keeps one artifact as the interchange contract
  rather than introducing a second. Concretely, the pipeline becomes: C# machine (source) -> `machine.json`
  (IR, generated) -> `generate.mjs` (unchanged consumer) -> per-language code.
- **Decision B: the vocabulary boundary.** Recommended: ship the ~8 primitives in §3, everything else is a
  named hand-written handler, and migrations stay named lambdas. Revisit only with a real third machine
  (RFC's Phase 1), not speculatively.
- **Decision C: round-trip attachment shape.** Recommended per RFC §6: C# `partial` + generated interface;
  TS generated interface + registry + a separate hand-owned impl file. Confirm on Increment C.
- **Decision D: oracle flip timing.** Recommended: keep TS as oracle through Increments A-B, flip in D. Do
  not flip early; it multiplies the moving parts during the riskiest work.
- **Decision E: versioning and migration. DEFERRED (2026-08-11).** Machines are effectively single-version
  for now. A stored snapshot whose version does not match the current definition is *rejected* as a typed
  `version-mismatch` (the engine's existing behavior when no migration is registered) and the client starts
  fresh. No `MigrateFrom` in the near-term authoring API, no migration section in the IR. This is a free
  simplification: the reject-old-drafts posture needs no new code, and migrations are additive, re-addable
  later without restructuring. The already-built migration golden and the sample's checkout v2 stay green;
  deferral means not building *more* migration machinery, not removing what exists. Accepted cost: a schema
  change invalidates in-flight drafts (fine for a POC, revisited before a product with valuable persisted
  drafts).

## 7. Recommended first build: Increment A

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
bare:

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
    m.StartsAt(TurnstileState.Locked);                          // id derived from the class name

    m.In(TurnstileState.Locked)                                 // empty context
        .On(TurnstileTrigger.Coin).WithInput<CoinInput>()
            .When(i => i.Coin, Is.OneOf("quarter", "dollar"))   // i.Coin, not "coin"
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
still extracts the field name as data, but authoring is refactor-safe with zero structural strings. Three
strings removed for free along the way: the effect key (`RunsOnce<TEffect>()` derives it from the machine id
+ trigger), the machine id (from the class name), and named-handler names (a `nameof` / method reference).
The state-machine `IEffect` is renamed `ISnapshotEffect` to stop colliding, in a reader's head, with the core
Trax.Effect abstractions.

Two things that keep it straightforward:

- **The initial context is derived from the initial state's schema** (`Array()` -> `[]`, nullable -> `null`,
  `Number()` -> `0`), so `StartsAt(state)` needs no factory in the common case. A factory stays available as
  an override for non-default seeds.
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
| &nbsp;&nbsp;· `m.Id("x").StartsAt(State.Initial)` | `Version()` optional (defaults to 1; rarely touched with migration deferred) |
| &nbsp;&nbsp;· `.Context(...)` per state | the declarative context schema |
| &nbsp;&nbsp;· `.In(s).On(t)...To(s)` | the transitions |
| &nbsp;&nbsp;· `.When(...)`, `.Reduce(...)` | declarative helpers, or a `named("x", lambda)` for the rare custom case |
| &nbsp;&nbsp;· `.Committed()`, `.RunsOnce<TEffect>(...)` | committed states and effect bindings |
| &nbsp;&nbsp;· `.Sample(t, input)` / `.Seed(s, ctx)` | differential probe inputs (test data; until probe-gen exists) |

Plus, only when they apply:
- **Effect** (if the machine has one): `interface IX : IEffect` + a class implementing it (server code).
- **Named custom handler bodies per frontend language**: only the bodies of any `named(...)` guards/reducers,
  hand-written in each frontend language. Often **zero**.

One-time, not per machine:
- **Host wiring**: `AddTraxStateMachines(assembly)`, an `ISnapshotPrincipal` (maps auth to a user key), and
  the effect DI registrations.
- **The frontend UI** that drives the machine (app code, uses the generated transport; not machine-shaped).

Everything else is **generated from that one C# file**:

| Generated (from the C# machine) | For |
|---|---|
| the IR (`machine.json`) + `structure.json` | the interchange contract + its derivable golden |
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
