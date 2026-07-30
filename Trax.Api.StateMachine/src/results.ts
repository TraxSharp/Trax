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
