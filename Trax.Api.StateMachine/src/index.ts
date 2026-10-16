/**
 * `@traxsharp/state-machine` — the TypeScript half of the Trax portable snapshot state machine.
 *
 * A machine's behavior is authored once in C# (`Trax.Effect.StateMachine`) and exported as a neutral
 * IR; this package interprets that IR so a browser and the server enforce the same guards, and a
 * shared differential corpus proves the two runtimes agree. Everything below is pure — no I/O. The
 * server-facing client lives behind `@traxsharp/state-machine/client`, the React binding behind
 * `@traxsharp/state-machine/react`.
 *
 * The exports are grouped by the layer you are working at, outermost first:
 *
 *   1. The wire        what crosses the network or lands in a row, and the codes that describe it
 *   2. The engine      the reducer that moves a snapshot from one state to the next
 *   3. The typed view  the same engine with a machine's own states/triggers/contexts as types
 *   4. Driving a UI    a store for when the machine owns the data, a form view for when the UI
 *                     does, and problems shaped for display
 *   5. Building one    the IR, the rule interpreter, and the generators that emit a twin
 *   6. Conformance     enumerating a machine into the golden corpus the other runtime replays
 *   7. Sample machines the two machines this repo ships as proofs
 */

// ── 1. The wire ───────────────────────────────────────────────────────────────────────────────────
// A Snapshot is the ONLY thing persisted or transported: `{ machine, version, state, context }`, with
// context canonicalized per RFC 8785 so both runtimes serialize byte-identically. The result codes are
// contract — the C# engine returns the same strings — while `detail` text is free to differ.

export type { Snapshot, MachineDefinition, TransitionDefinition, MigrationResult, MachineTimer } from './types';
export type { Guard, Reduce, ContextValidator, InputValidator } from './types';
export {
  RefusalCode,
  ClientProblemCode,
  type AdvanceResult,
  type RehydrationResult,
} from './results';

// ── 2. The engine ─────────────────────────────────────────────────────────────────────────────────
// The reducer itself: string-based, total (no operation throws), and the exact twin of C#'s
// SnapshotMachine. Illegal (state, context) pairs are rejected rather than represented.

export { SnapshotMachine } from './machine';

// ── 3. The typed view ─────────────────────────────────────────────────────────────────────────────
// The same engine with one machine's alphabet as types: `context` is discriminated by `state`, and a
// trigger's input is required-or-forbidden at compile time. This is what a generated twin exports.

export { TypedMachine, typedMachineFromIr } from './typed';
export type {
  MachineSpec,
  StateOf,
  TriggerOf,
  ContextOf,
  InputOf,
  InputArgs,
  TypedSnapshot,
  TypedAdvanceResult,
  TypedRehydrationResult,
} from './typed';

// ── 4. Driving a UI ───────────────────────────────────────────────────────────────────────────────
// A framework-free store over a TypedMachine: it holds the snapshot, transitions it, and re-renders on
// a REJECTION too, so a declined action is surfaced rather than silently dropped. `Problem` is the
// display-shaped view of a rejection. The React hook over this is at `@traxsharp/state-machine/react`.

export {
  MachineController,
  type ControllerOptions,
  type ControllerView,
  type MachineObserver,
} from './controller';
export { problemFromAdvance, problemFromRehydration, type Problem } from './problem';
export { createDevLogger, type DevLoggerOptions } from './devtools';

// The other shape: the UI already owns the data (a form), and the machine is asked rather than told.
// A FormView derives the whole bridge — step↔state both ways, snapshot building, advance/can/serialize,
// resume — from a step map and one projection.
export { createFormView } from './formView';
export type { FormView, FormViewOptions, ResumedStep, StepAdvance } from './formView';

// ── 5. Building a machine from its IR ─────────────────────────────────────────────────────────────
// `machineFromIr` runs the IR's declarative guards/reducers through the rule interpreter — no
// hand-written logic per language. The generators emit the twin a frontend imports; regeneration and
// the CI drift check call these same two functions, so they cannot disagree.

export { machineFromIr } from './rules/irMachine';
export type { IrDocument, IrTransition, IrOutcome, IrOutcomeEdge, IrTimer } from './rules/irMachine';
export { evaluateRule, applyReduction, validateSchema, validateInput } from './rules/interpreter';
export type {
  Rule,
  Reduction,
  ValueSource,
  SetStep,
  ContextSchema,
  FieldSchema,
  ValueSchema,
  RuleSource,
  JsonFieldType,
  CompareOp,
  CustomGuards,
  CustomReducers,
} from './rules/interpreter';
export { generateContextTypes, generateMachineFactory } from './rules/generateTypes';

// The client format: what code `trax client` generates checks the installed package serves, when it loads.
export { clientFormat, oldestClientFormatServed, requireClientFormat } from './clientFormat';
export type { MachineFactoryOptions } from './rules/generateTypes';

// ── 6. Cross-runtime conformance ──────────────────────────────────────────────────────────────────
// Enumerate a machine's reachable behavior into a golden corpus that the C# engine replays
// (Trax.Effect.StateMachine.Testing). TypeScript is currently the oracle.

export { enumerate, serializeCorpus } from './differential';
export type { DifferentialSpec, DifferentialCorpus, DifferentialCase } from './differential';
