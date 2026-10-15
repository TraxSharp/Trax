import type { ProblemCode } from '../results';
/**
 * The client half of "one set of operations, any machine".
 *
 * `Trax.Effect.StateMachine.Persistence` serves six generic mutations, when the host opts in to them —
 * `startSnapshot`, `saveSnapshot`, `advanceSnapshot`, `loadSnapshot`, `sendSnapshot`, `discardSnapshot` —
 * under `dispatch { stateMachine { … } }`, and every one carries a `machine` discriminator the server
 * resolves in its registry. There is no per-machine
 * mutation fan, so there is no per-machine client either: this module knows nothing about any specific
 * machine. The machine is an argument and the snapshot is opaque canonical JSON on the wire.
 *
 * How the operation reaches the server is the host's business — see {@link SnapshotExecutor}.
 */

/** A typed refusal returned as DATA (HTTP 200, no GraphQL `errors`), never a thrown transport failure. */
export interface SnapshotProblem {
  /** A contract code: `GUARD_FAILED`, `INVALID_CONTEXT`, `SCHEMA_MISMATCH`, `DRAFT_COMMITTED`, … */
  code: ProblemCode;
  message: string;
}

/** The `output` every mutation returns: the snapshot as JSON, or a problem. */
export interface SnapshotOutput {
  snapshot: string | null;
  problem: SnapshotProblem | null;
  /** `startSnapshot` only: the id the server minted for the new instance. */
  id?: string | null;
  /** `startSnapshot` only: true when this request created the instance, false when it replayed a start. */
  created?: boolean | null;
  /** `discardSnapshot` only: true when the instance is gone (already gone, or another user's, included). */
  discarded?: boolean;
  /**
   * The instance's revision after the operation, bumped by every write, when the server reports one (the typed
   * `machines` root does). It travels beside the snapshot, never in it.
   */
  revision?: number | null;
  /**
   * When the timer of the state the instance is in fires (ISO-8601), when the server reports it (the typed `machines`
   * root does, if the document selects it); null when that state has no timer or the server cleared it. Absent when
   * the answer cannot know it: a replay of a request the instance has moved past since. It travels beside the
   * snapshot, never in it.
   */
  dueAt?: string | null;
}

export type SnapshotOperation =
  | 'startSnapshot'
  | 'saveSnapshot'
  | 'advanceSnapshot'
  | 'loadSnapshot'
  | 'sendSnapshot'
  | 'discardSnapshot';

/**
 * Runs one of the six generic mutations and returns its `output`. This is the seam between the engine and the
 * host's GraphQL stack: an app with generated typed documents (Apollo, urql, …) maps the operation name
 * onto its own document, and an app with none can use {@link createHttpExecutor}.
 *
 * It may throw — the transport is allowed to fail. Callers above ({@link SnapshotClient}) turn a throw
 * into a typed problem so no caller has to write a try/catch of its own.
 */
export type SnapshotExecutor = (
  operation: SnapshotOperation,
  input: Record<string, unknown>,
) => Promise<SnapshotOutput | null | undefined>;

/** The operations, machine-agnostic: every method takes the machine (and the draft id) it acts on. */
export interface SnapshotClient {
  /**
   * Create an instance from the machine's declared start. The server mints the id (in the output's `id`);
   * `requestId` makes a retry return the same instance instead of creating a second one.
   */
  start(machine: string, requestId: string, options?: StartOptions): Promise<SnapshotOutput>;
  save(machine: string, id: string, snapshot: string, schemaHash?: string): Promise<SnapshotOutput>;
  advance(
    machine: string,
    id: string,
    trigger: string,
    options?: AdvanceOptions,
  ): Promise<SnapshotOutput>;
  load(machine: string, id: string, schemaHash?: string): Promise<SnapshotOutput>;
  send(machine: string, id: string, requestId?: string, schemaHash?: string): Promise<SnapshotOutput>;
  /** Delete one of the caller's instances. Idempotent: one already gone answers `discarded: true`. */
  discard(machine: string, id: string, requestId: string): Promise<SnapshotOutput>;
}

export interface StartOptions {
  /** The start input as a JSON object, when the machine's start takes one. */
  input?: string;
  /** The machine's schema hash, for the server's version-skew check. */
  schemaHash?: string;
}

export interface AdvanceOptions {
  /** Trigger input as JSON, when the trigger takes one. */
  input?: string;
  /** Idempotency key, so a network retry of this advance replays instead of firing twice. */
  requestId?: string;
  /** The machine's schema hash, for the server's version-skew check. */
  schemaHash?: string;
  /**
   * The snapshot the LOCAL twin computed for this same advance, as canonical JSON. When present the
   * server compares it against its own authoritative result and refuses a divergence with
   * `CLIENT_DIVERGENCE`, rather than storing a snapshot the two engines don't agree on.
   */
  clientResult?: string;
}

const EMPTY: SnapshotOutput = { snapshot: null, problem: null };

/**
 * Bind the operations to an executor. Nothing here is machine-specific; {@link createDraftSession}
 * is the layer that pins one (machine, id) pair.
 *
 * `schemaHash` is threaded through every call because the server's version-skew handshake is per-request:
 * a client whose machine has drifted from the deployed one is refused with `SCHEMA_MISMATCH` (telling it
 * to reload) instead of writing under an outdated contract. Omitting it opts out of the check.
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#talking-to-a-server
 */
export function createSnapshotClient(execute: SnapshotExecutor): SnapshotClient {
  const run = async (
    operation: SnapshotOperation,
    input: Record<string, unknown>,
  ): Promise<SnapshotOutput> => (await execute(operation, input)) ?? EMPTY;

  return {
    start: (machine, requestId, options = {}) =>
      run('startSnapshot', {
        machine,
        requestId,
        input: options.input,
        schemaHash: options.schemaHash,
      }),

    save: (machine, id, snapshot, schemaHash) =>
      run('saveSnapshot', { machine, id, snapshot, schemaHash }),

    advance: (machine, id, trigger, options = {}) =>
      run('advanceSnapshot', {
        machine,
        id,
        trigger,
        input: options.input,
        requestId: options.requestId,
        schemaHash: options.schemaHash,
        clientResult: options.clientResult,
      }),

    load: (machine, id, schemaHash) => run('loadSnapshot', { machine, id, schemaHash }),

    send: (machine, id, requestId, schemaHash) =>
      run('sendSnapshot', { machine, id, requestId, schemaHash }),

    discard: (machine, id, requestId) => run('discardSnapshot', { machine, id, requestId }),
  };
}

/** How a request reaches the host: the headers each one carries, and the `fetch` it is sent with. */
export interface HttpExecutorOptions {
  /** Extra headers per request — auth, typically. Called per call so a rotating token stays fresh. */
  headers?: () => Record<string, string>;
  /** Defaults to the global `fetch`. */
  fetch?: typeof globalThis.fetch;
}

