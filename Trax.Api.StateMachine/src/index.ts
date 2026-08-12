// Core engine (string-based, wire-level) — the twin of Trax.Effect.StateMachine.
export type { Snapshot, MachineDefinition, TransitionDefinition, MigrationResult, Guard, Reduce, ContextValidator } from './types';
export { RejectionReasons, RehydrationErrorCodes, type AdvanceResult, type RehydrationResult } from './results';
export { SnapshotMachine } from './machine';

// Typed frontend surface (framework-free). The React hook is at the `./react` subpath.
export { TypedMachine } from './typed';
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
export { MachineController, type ControllerOptions, type ControllerView, type MachineObserver } from './controller';
export { problemFromAdvance, problemFromRehydration, type Problem } from './problem';
export { createDevLogger, type DevLoggerOptions } from './devtools';

// Cross-runtime differential conformance: enumerate a machine's behavior into a golden corpus that the C#
// engine replays (Trax.Effect.StateMachine.Testing). TypeScript is the oracle.
export { enumerate, serializeCorpus } from './differential';
export type { DifferentialSpec, DifferentialCorpus, DifferentialCase } from './differential';

// IR-driven machines: build a runnable machine from the IR a C# machine exports, the rule/reduction data it
// carries, and the generators that emit a typed machine from it. This is what an app-level generated machine
// (`<machine>.machine.g.ts`) imports, so the whole surface is reachable from the package entry, not just the
// vendored samples.
export { machineFromIr } from './rules/irMachine';
export type { IrDocument, IrTransition } from './rules/irMachine';
export { evaluateRule, applyReduction, validateSchema } from './rules/interpreter';
export type {
  Rule,
  Reduction,
  ValueSource,
  SetStep,
  ContextSchema,
  FieldSchema,
  RuleSource,
  JsonFieldType,
  CompareOp,
  CustomGuards,
  CustomReducers,
} from './rules/interpreter';
export { generateContextTypes, generateMachineFactory } from './rules/generateTypes';

// Machines.
export { turnstile, turnstileCore, turnstileDefinition } from './machines/turnstile/turnstile';
export type { TurnstileState, TurnstileTrigger, TurnstileSpec } from './machines/turnstile/turnstile';
export { checkout, checkoutCore, checkoutDefinition } from './machines/checkout/checkout';
export type { CheckoutState, CheckoutTrigger, CheckoutSpec } from './machines/checkout/checkout';
