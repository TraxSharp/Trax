---
title: State machines
description: "How Trax state machines work: the definition and snapshot documents, the three guarantees, matching C# and TypeScript runtimes, and exactly-once effects."
type: explanation
area: machines
nav_order: 1
source:
  - Trax.Effect/src/Trax.Effect.StateMachine
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence
---

# State machines

Trax.Effect.StateMachine represents a multi-step flow (a wizard, an application form, a checkout) as a
small, language-neutral snapshot, so a C# backend and a TypeScript client agree on where a user is in the
flow and the data that goes with it. The whole runtime state of an instance is a serializable snapshot that
can be stored in Postgres and rebuilt from any point, so a user can start on one device and finish on
another. Illegal (state, data) combinations are made unrepresentable, and neither the API nor the client can
produce an unhandled exception.

To build one, follow [Author and serve a machine](/docs/statemachine/authoring). Guards and reducers written as data
are covered in [Write guards and reducers as data](/docs/statemachine/declarative-authoring), and a state that runs
a train in [Invoke a train from a state](/docs/statemachine/invoking-trains).

## The two-document model

A machine has two documents, and most mistakes come from treating them as one.

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

1. **Resumable from any point.** A snapshot fully determines an instance. Rebuilding it is a `Rehydrate` of the
   stored JSON. There is no hidden in-memory state to reconstruct.
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
(`@traxsharp/state-machine`). They are kept identical not by generating one engine from the other but by a shared set of
language-neutral conformance fixtures that both engines drive and must agree on. Only the result codes are contract;
human-readable detail text is free to differ. [Result codes](/docs/sdk-reference/statemachine-api/result-codes)
lists every code a client can receive, the engine's and the persistence layer's.

The machine is authored in C#, which is the source of truth, and exported to a neutral
[IR](/docs/sdk-reference/statemachine-api/ir-format) (`<machine>.ir.json`). Common guards and reducers are authored
declaratively and travel in the IR as data; a small interpreter on each side runs them, so they are single-sourced
rather than hand-written per language. A guard or reducer the vocabulary cannot express is bound by name in the IR
and hand-written once per runtime: in C# with `CustomGuard(name, fn)` / `CustomReducer(name, fn)`, in TypeScript
with `customGuards` / `customReducers`. A C# machine that names a custom rule or reduction with no handler bound
fails at `Build()`, rather than refusing that edge forever. A guard or reducer written as a plain C# delegate cannot
be carried by the IR at all, so export refuses a declarative machine that still has one, naming the edge, rather
than emit it as an unconditional edge (see
[Delegate vs declarative](/docs/statemachine/delegates-and-declarative-rules)). The
snapshot itself carries only structure and data, never logic.

Agreement on structure is not enough on its own, because a custom guard exists once per language and the two
interpreters must agree too. Two committed goldens, the differential corpus and the migration golden, are replayed by
both engines and compared byte for byte; [The codegen pipeline](/docs/statemachine/codegen-pipeline) explains how
they are produced and what each one catches. A byte-for-byte compare needs one serialization that both engines emit
whatever built the snapshot: the [canonical wire](/docs/statemachine/canonical-wire), RFC 8785 with the
`JSON.stringify` number and string rules. It is also what makes a hash or a signature over a stored snapshot
meaningful.

## Persistence and exactly-once effects

The persistence layer is generic over a machine's (state, trigger) pair and stores the context in a real
`jsonb` column. Its tables (`snapshot_draft` and `effect_claim`) ship as migrations in the core data
providers, so registering `UsePostgres` or `UseSqlite` creates them. There is no manual DDL and no `EnsureCreated`
step. A draft is scoped to its owner by a composite key, and every authoritative write carries an app-managed
optimistic-concurrency token, so concurrent writers get a typed conflict rather than a lost update or a thrown
exception.

Two write paths have different trust levels. Autosave is the soft path: the client sends a whole snapshot, the
server validates it and stores it. Advance is the authoritative path: the client sends only a trigger, and the
server re-drives the transition from the stored snapshot, never trusting a client-computed state. The soft path
serves only machines with no server-side behaviour: a machine with an event or a start gated by a role or a policy,
a timer, an invoked train or an effect refuses every save as `save-not-allowed`, and moves only through its declared
events.

Autosave stores the context the client wrote, so any value an effect will act on has to be held to its true value
by the state's rule, not just to its type. A checkout whose context carries a `total` holds
`total == items x price` in every state; checking only that `total` is a number would let a browser save two items
at one cent and have the charge take one cent. The [State Machine sample](/docs/samples/state-machine) shows the
check and its test.

Some transitions carry an irreversible side effect: charge a card, send a letter, provision a resource. Those run
exactly once per intent, even under retries, two devices, or a crash mid-flight. A send marks the draft's row before
it runs the effect, so two concurrent sends deliver once and nothing else writes the draft while the effect runs;
the receipt, the move and the end of the mark are one write, and a retry replays the recorded result. A lease with a
fence token keeps the mark live: if a runner marks the draft and then dies, the lease expires and the next send, or
the outcome reconciler on any host that registers the effect, takes the mark over and runs the effect again under the
same key, which the effect passes to the system it calls. A revived stuck runner is fenced out of recording over the
new claimant's work. [Effects](/docs/sdk-reference/statemachine-api/i-snapshot-effect) is the reference.

Outside a machine, the same guarantee is `IdempotentEffect`, over the `effect_claim` ledger. There, reclaim happens
on demand, on the next attempt at that intent, so an abandoned claim stays in `effect_claim` until someone tries
again. `EffectClaimSweeper` releases every claim whose lease has expired, and Trax neither registers nor schedules
it: a host that wants that backstop runs it itself, for example from a scheduled train, with the scoped
`IEffectClaimStore` that `AddStateMachines` registers.

A draft has no natural end. A user can abandon a half-filled form, and a completed one lingers as a committed
snapshot. A background sweep on every host that registers machines deletes a user's draft once it has been idle past
`DraftTtl` or has lived past `MaxDraftLifetime`, so a returning user starts fresh rather than wedged behind a finished
one. The sweep never deletes the record of something the server did (an instance in a state only the server puts it
in), and a load never deletes. The defaults and every exception are under
[AddStateMachines options](/docs/sdk-reference/statemachine-api/add-state-machines#options).

## The frontend surface

A React engineer works through one hook. It owns the snapshot, persists after every successful step, and
re-renders on every change, including a declined action, so the reason is never silently swallowed. Types
flow from the machine's spec: the current step is a typed string, the context is discriminated by that step,
and a trigger's input is required or forbidden at the type level. Persistence is driven by transitions, not by a
decoupled timer, which is what prevents a save from landing after the state has already advanced past a
terminal step. The [TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package) is the reference.

## Where the code lives

| Piece | Location |
| --- | --- |
| C# engine (pure, total, dependency-free) | Trax.Effect.StateMachine |
| C# persistence, exactly-once effects, mutation trains | Trax.Effect.StateMachine.Persistence |
| TypeScript engine, typed facade, React hook | the @traxsharp/state-machine package |
| Shared machines (the exported IR, differential and migration goldens) and the frontend generators | the shared machines directory and the codegen tools |
