import type { Snapshot } from './types';

/**
 * The reasons `advance` can decline to move. These strings are part of the cross-language contract:
 * the shared conformance fixtures assert on them, so the C# reducer uses the identical values.
 */
export const RejectionReasons = {
  NoTransition: 'no-transition',
  GuardFailed: 'guard-failed',
  InvalidContext: 'invalid-context',
  InternalError: 'internal-error',
  /** The trigger's input does not match the input schema the trigger declares. */
  InvalidInput: 'invalid-input',
  /** The trigger is an invoked train's outcome; only `applyOutcome` applies it, never `advance`. */
  OutcomeBound: 'outcome-bound',
  /** The trigger is a timer; only `applyTimer` fires it, never `advance`. */
  TimerBound: 'timer-bound',
  /**
   * A server refusal of `discard`, not of `advance`: the server put the instance in its state (an effect's record, an
   * invoked train's outcome, a gated event, carried on by a timer), it is in a state that invokes a train, or an effect
   * is pending on it, so its owner may not discard it.
   */
  DiscardNotAllowed: 'discard-not-allowed',
  /**
   * A server refusal: an effect is pending on the instance, so every other write to it waits until the effect is
   * recorded or fails. Retry once it has finished. Only the server answers it.
   */
  EffectInProgress: 'effect-in-progress',
  /** The input or the resulting snapshot is past the server's size cap. Only the server answers it. */
  TooLarge: 'too-large',
} as const;

/** The reasons `rehydrate` can reject stored JSON. Part of the cross-language contract. */
export const RehydrationErrorCodes = {
  Malformed: 'malformed',
  UnknownMachine: 'unknown-machine',
  VersionMismatch: 'version-mismatch',
  UnknownState: 'unknown-state',
  InvalidContext: 'invalid-context',
} as const;

/** Exactly one of transitioned or rejected — never a throw. */
export type AdvanceResult =
  | { outcome: 'transitioned'; snapshot: Snapshot }
  | { outcome: 'rejected'; reason: string; detail?: string };

/** The "parse, don't validate" boundary: raw JSON becomes a typed snapshot or a typed error. */
export type RehydrationResult =
  | { result: 'ok'; snapshot: Snapshot }
  | { result: 'error'; code: string; message: string };
