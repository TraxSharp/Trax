/**
 * The client half of "one set of operations, any machine".
 *
 * `Trax.Effect.StateMachine.Persistence` exposes exactly four mutations — `saveSnapshot`,
 * `advanceSnapshot`, `loadSnapshot`, `sendSnapshot` — under `dispatch { stateMachine { … } }`, and every
 * one carries a `machine` discriminator the server resolves in its registry. There is no per-machine
 * mutation fan, so there is no per-machine client either: this module knows nothing about any specific
 * machine. The machine is an argument and the snapshot is opaque canonical JSON on the wire.
 *
 * How the operation reaches the server is the host's business — see {@link SnapshotExecutor}.
 */

/** A typed refusal returned as DATA (HTTP 200, no GraphQL `errors`), never a thrown transport failure. */
export interface SnapshotProblem {
  /** A contract code: `guard-failed`, `invalid-context`, `schema-mismatch`, `draft-committed`, … */
  code: string;
  message: string;
}

/** The `output` every one of the four mutations returns: the snapshot as JSON, or a problem. */
export interface SnapshotOutput {
  snapshot: string | null;
  problem: SnapshotProblem | null;
}

export type SnapshotOperation =
  | 'saveSnapshot'
  | 'advanceSnapshot'
  | 'loadSnapshot'
  | 'sendSnapshot';

/**
 * Runs one of the four mutations and returns its `output`. This is the seam between the engine and the
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

/** The four operations, machine-agnostic: every method takes the machine and draft id it acts on. */
export interface SnapshotClient {
  save(machine: string, id: string, snapshot: string, schemaHash?: string): Promise<SnapshotOutput>;
  advance(
    machine: string,
    id: string,
    trigger: string,
    options?: AdvanceOptions,
  ): Promise<SnapshotOutput>;
  load(machine: string, id: string, schemaHash?: string): Promise<SnapshotOutput>;
  send(machine: string, id: string, requestId?: string, schemaHash?: string): Promise<SnapshotOutput>;
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
   * `client-divergence`, rather than storing a snapshot the two engines don't agree on.
   */
  clientResult?: string;
}

const EMPTY: SnapshotOutput = { snapshot: null, problem: null };

/**
 * Bind the four operations to an executor. Nothing here is machine-specific; {@link createDraftSession}
 * is the layer that pins one (machine, id) pair.
 *
 * `schemaHash` is threaded through every call because the server's version-skew handshake is per-request:
 * a client whose machine has drifted from the deployed one is refused with `schema-mismatch` (telling it
 * to reload) instead of writing under an outdated contract. Omitting it opts out of the check.
 */
export function createSnapshotClient(execute: SnapshotExecutor): SnapshotClient {
  const run = async (
    operation: SnapshotOperation,
    input: Record<string, unknown>,
  ): Promise<SnapshotOutput> => (await execute(operation, input)) ?? EMPTY;

  return {
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
  };
}

// ── The zero-dependency executor ──────────────────────────────────────────────────────────────────

const OUTPUT = 'output { snapshot problem { code message } }';

const MUTATIONS: Record<SnapshotOperation, string> = {
  saveSnapshot: `mutation($i: SaveSnapshotInput!){ dispatch { stateMachine { saveSnapshot(input:$i){ ${OUTPUT} } } } }`,
  advanceSnapshot: `mutation($i: AdvanceSnapshotInput!){ dispatch { stateMachine { advanceSnapshot(input:$i){ ${OUTPUT} } } } }`,
  loadSnapshot: `mutation($i: LoadSnapshotInput!){ dispatch { stateMachine { loadSnapshot(input:$i){ ${OUTPUT} } } } }`,
  sendSnapshot: `mutation($i: SendSnapshotInput!){ dispatch { stateMachine { sendSnapshot(input:$i){ ${OUTPUT} } } } }`,
};

export interface HttpExecutorOptions {
  /** Extra headers per request — auth, typically. Called per call so a rotating token stays fresh. */
  headers?: () => Record<string, string>;
  /** Defaults to the global `fetch`. */
  fetch?: typeof globalThis.fetch;
}

/**
 * An executor over plain `fetch` for a host with no GraphQL client. It builds the four documents itself,
 * so nothing outside this file needs to know their text. A host that already has generated typed
 * documents should write a five-line executor against those instead of using this.
 */
export function createHttpExecutor(
  endpoint: string,
  options: HttpExecutorOptions = {},
): SnapshotExecutor {
  const doFetch = options.fetch ?? globalThis.fetch;

  return async (operation, input) => {
    const response = await doFetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...options.headers?.() },
      body: JSON.stringify({ query: MUTATIONS[operation], variables: { i: input } }),
    });

    const body = (await response.json()) as {
      data?: { dispatch?: { stateMachine?: Record<string, { output?: SnapshotOutput }> } };
      errors?: { message: string }[];
    };

    // A GraphQL error is transport-level (an auth refusal arrives this way, at HTTP 200) — distinct from
    // a typed problem, which is data. Throwing keeps that distinction: the session maps it to a problem.
    if (body.errors?.length) throw new Error(body.errors[0].message);
    return body.data?.dispatch?.stateMachine?.[operation]?.output ?? null;
  };
}
