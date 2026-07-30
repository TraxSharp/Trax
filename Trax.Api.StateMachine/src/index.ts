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

// Machines.
export { turnstile, turnstileCore, turnstileDefinition } from './machines/turnstile/turnstile';
export type { TurnstileState, TurnstileTrigger, TurnstileSpec } from './machines/turnstile/turnstile';
