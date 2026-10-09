---
layout: default
title: State Machines
description: "Trax.Effect.StateMachine overview: the snapshot and definition documents, the three guarantees, matching C# and TypeScript runtimes, and exactly-once effects."
nav_order: 8
has_children: true
section: Packages
---

# State Machines

Trax.Effect.StateMachine represents a multi-step flow (a wizard, an application form, a checkout) as a
small, language-neutral snapshot, so a C# backend and a TypeScript client agree on where a user is in the
flow and the data that goes with it. The whole runtime state of an instance is a serializable snapshot that
can be stored in Postgres and rebuilt from any point, so a user can start on one device and finish on
another. Illegal (state, data) combinations are made unrepresentable, and neither the API nor the client can
produce an unhandled exception.

To author a machine and wire it into a host, start with [Authoring a machine](/docs/statemachine/authoring).
To author it as data (guards and reducers the engine can export to an IR and generate the frontend from), see
[Declarative authoring](/docs/statemachine/declarative-authoring). For a state that runs a train and routes its
outcome, see [Invoking a train](/docs/statemachine/invoking-trains).

## The two-document model

Keep these two things separate. Conflating them is the classic mistake.

| | Definition | Snapshot |
| --- | --- | --- |
| What it is | the machine's states, transitions, guards, and per-state context rules | one instance's current position and data |
| Where it lives | in code (a C# or TypeScript `MachineDefinition`) | in data (a Postgres row, a GraphQL payload) |
| How often it changes | rarely (a deploy) | constantly (every user action) |
| Crosses the wire | no | yes |

The definition is the program. The snapshot is the value it operates on. A snapshot has exactly four fields,
always in this order: `machine`, `version`, `state`, `context`. `state` is the current step. `context` is
the data for that step, and only that step, so its shape is discriminated by `state`.

## The three guarantees

1. **Resumable from any point.** A snapshot fully determines an instance. Rebuilding it is just a
   `Rehydrate` of the stored JSON. There is no hidden in-memory state to reconstruct.
2. **Illegal states unrepresentable.** Each state validates its own context on the way in (rehydrate) and on
   the way out (advance). A snapshot that claims a state but carries the wrong data is rejected as a typed
   error, never quietly accepted.
3. **No unhandled exceptions.** Every operation is total. `Advance` returns a transitioned or rejected
   result. `Rehydrate` returns an ok or error result. An unpermitted trigger, a failed guard, or malformed
   stored JSON all become typed values, never a throw. That includes JSON that parses but cannot be kept:
   `Rehydrate` refuses a number outside the range of a double (`1e400`) and a NUL character as `malformed`,
   so an accepted snapshot always has a canonical wire and always fits a `jsonb` column.

A state or trigger token is matched by its exact declared name. `"1"`, `" Unlocked"` and `"Locked, Unlocked"`
are not aliases of `Unlocked`, as they would be to `Enum.TryParse`, and a rehydrated snapshot always carries
the declared name, so the value that is stored and served is canonical.

## Two runtimes, one behavior

The engine is implemented twice, once in C# (`Trax.Effect.StateMachine`) and once in TypeScript
(`@trax/state-machine`). They are kept identical not by generating one from the other but by a shared set of
language-neutral conformance fixtures that both engines drive and must agree on. Only the engine's result
codes (`no-transition`, `guard-failed`, `invalid-context`, `internal-error`, `malformed`, `unknown-machine`,
`version-mismatch`, `unknown-state`) are contract. Human-readable detail text is free to differ. The snapshot
mutations add their own codes on top (`not-found`, `conflict`, `unauthenticated` and more); the
[result codes](/docs/sdk-reference/statemachine-api/result-codes) page lists every code a client can receive.

The machine is authored in C#, which is the source of truth, and exported to a neutral IR
(`<machine>.ir.json`). Common guards and reducers are authored declaratively and travel in the IR as data;
a small interpreter on each side runs them, so they are single-sourced rather than hand-written per language.
A genuinely-custom guard or reducer is bound by name in the IR and hand-written per runtime (the escape
hatch): in C# with `CustomGuard(name, fn)` / `CustomReducer(name, fn)` on the builder, in TypeScript with
`customGuards` / `customReducers`. A C# machine that names a custom rule or reduction with no handler bound
fails at `Build()`, rather than refusing that edge forever. A guard or reducer written as a plain C# delegate
(`When(Func...)`, `Reduce(Func...)`) cannot be carried by the IR at all, so export refuses a declarative machine
that still has one, naming the edge, rather than emit it as an unconditional edge (see
[Delegate vs declarative](/docs/sdk-reference/statemachine-api/fluent-authoring#delegate-vs-declarative)). The snapshot itself still carries only structure and data, never logic. A machine's structure and its
declarative logic are generated for the frontend from that IR, and a drift check fails the build if a
committed generated file goes stale.

Structure agreement is not enough on its own: any custom (hand-written) guard or reducer exists once per
language, and the interpreters that run the declarative rules must agree too, so behavior can still drift.
That is caught by an exhaustive **differential corpus**. TypeScript is the oracle:
it drives the engine over every reachable snapshot (discovered by walking the machine's own transitions from
the initial snapshot, plus a few declared `seeds` for states whose context arrives out of band), times every
trigger, times a few representative `samples` per trigger, and records each outcome as canonical wire (on a
transition) or a rejection code. The corpus is committed as `machines/<machine>/differential.json`; both
engines replay it and must reproduce every outcome byte-for-byte. Turnstile is 18 cases, checkout 30, most
of them rejections, which is exactly what a hand-written fixture set never covers exhaustively.

The corpus is machine-managed; you never hand-write it. The `samples` and `seeds` are authored in C# with
`.Differential(...)` and exported into the IR's `differential` block. Regenerate deliberately, and the git diff of the golden is the review of what changed. Each side replays the committed file independently, so the C# suite needs no Node and the
TypeScript suite needs no .NET.

The **canonical wire** is what makes a byte-for-byte comparison meaningful. The envelope (`machine`,
`version`, `state`, `context`) is emitted in fixed order; the context is canonicalized per RFC 8785 (JCS):
object keys sorted by UTF-16 code unit (recursively, with array order preserved), numbers formatted by the
ECMAScript `Number` algorithm (so `1e21` is `1e+21`, not .NET's `1E+21`), and strings escaped exactly as
`JSON.stringify` (non-ASCII and valid surrogate pairs stay literal; control characters use the short escapes
or lowercase `\u00xx`, and a lone surrogate, which has no UTF-8 encoding, is escaped as lowercase `\udxxx`).
Integer-like keys are ordered like any other (`"10"` before `"2"`), and a key named `__proto__` is an
ordinary key.
Both engines emit identical bytes regardless of how the snapshot was constructed, which is the prerequisite
for the differential's byte-exact compare and for any hash or signature over a stored snapshot.

Forward migration of stored snapshots is guarded the same way. When a machine bumps its `version`, a
per-source-version migration function upgrades an older stored snapshot on rehydrate (a missing step in the
chain is a typed `version-mismatch`, never a silent misread). Correctness against real stored shapes is pinned
by a **migration golden**, `machines/<machine>/migration.json`: a committed set of stored older-version
snapshots and the exact canonical wire each must become. Both engines replay it, so a migration that drops,
renames, or reorders a surviving field fails, and the two runtimes cannot migrate the same draft differently.
Where the differential guards machine logic, the migration golden guards schema evolution.

## Persistence and exactly-once effects

The persistence layer is generic over a machine's (state, trigger) pair and stores the context in a real
`jsonb` column. Its two tables (`snapshot_draft` and `effect_claim`) ship as migrations in the core data
providers, so registering `UsePostgres` or `UseSqlite` creates them automatically. There is no manual DDL and
no `EnsureCreated` step. A draft is scoped to its owner by a composite key, and every authoritative write
carries an app-managed optimistic-concurrency token, so concurrent writers get a typed conflict rather than a
lost update or a thrown exception. Two write paths have different trust levels. Autosave is the soft path: the
client sends a whole snapshot, the server validates it and stores it. Advance is the authoritative path: the
client sends only a trigger, and the server re-drives the transition from the stored snapshot, never
trusting a client-computed state.

Autosave stores the context the client wrote, so any value an effect will act on must be held to its
true value by the state's `Holds`, not just to its type. A checkout whose context carries a `total`
holds `total == items x price` in every state; checking only that `total` is a number would let a
browser save two items at one cent and have the charge take one cent. The
[State Machine sample](/docs/samples/state-machine) shows the check and its test.

Some transitions carry an irreversible side effect: charge a card, send a letter, provision a resource.
Those must run exactly once per intent, even under retries, two devices, or a crash mid-flight. The core
provides a generic exactly-once runner keyed on an intent that names the action, not its content. It claims
the intent before running the effect, so two concurrent sends deliver once and a crash-retry replays the
recorded result. A lease with a fence token keeps it live: if a runner wins the claim and then dies, the
lease expires and the next caller reclaims the key, and a revived stuck runner is fenced out of completing
the new claimant's work. Reclaim happens on demand, on the next attempt at that intent, so nothing runs in
the background: an abandoned claim stays in `effect_claim` until someone tries again. `EffectClaimSweeper`
releases every claim whose lease has expired, but Trax neither registers nor schedules it. If you want that
backstop, run it yourself, for example from a scheduled train:
`await new EffectClaimSweeper(claimStore).Sweep(DateTimeOffset.UtcNow)`, with the scoped `IEffectClaimStore`
that `AddStateMachines` registers.

A draft has no natural end. A user can abandon a half-filled form, and a completed one lingers as a committed
snapshot. An optional TTL bounds that: set `DraftTtl` on `AddStateMachines`, and the next load of a draft
idle past the window discards it. The row is deleted and the load reports no draft, so the user starts fresh.
Deleting rather than ignoring also clears a committed draft, so a returning user is never wedged behind a
finished one. The check is lazy, on load, so there is no scheduled job, and it never touches an active
session: an advance or autosave mid-flow is left alone. The default is off, which never expires a draft.

## The frontend surface

A React engineer works through one hook. It owns the snapshot, persists after every successful step, and
re-renders on every change, including a declined action so the reason is never silently swallowed. Types
flow from the machine's spec: the current step is a typed string, the context is discriminated by that step,
and a trigger's input is required or forbidden at the type level. Persistence is transition-driven, not a
decoupled timer, which is what prevents a save from landing after the state has already advanced past a
terminal step.

## Where the code lives

| Piece | Location |
| --- | --- |
| C# engine (pure, total, dependency-free) | Trax.Effect.StateMachine |
| C# persistence, exactly-once effects, mutation trains | Trax.Effect.StateMachine.Persistence |
| TypeScript engine, typed facade, React hook | the @trax/state-machine package |
| Shared machines (the exported IR, differential and migration goldens) and the frontend generators | the shared machines directory and the codegen tools |
