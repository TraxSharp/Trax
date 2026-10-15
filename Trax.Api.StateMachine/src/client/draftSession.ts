/**
 * One user's one draft of one machine, over the machine's persisted documents (an executor from
 * `createMachineExecutor`), and {@link startDraft}, which creates one. The session is a handle already told which
 * machine and which draft it is looking at, so the wizard code never repeats either.
 *
 * The other thing it adds is TOTALITY at the edge. The server answers a refusal as data (a typed
 * `problem`) but the network can still fail, so a caller that wanted to know "did my draft save?" would
 * otherwise need a try/catch at every call site. Every method here returns a {@link DraftResult}
 * instead: nothing throws, and an unreachable server is just another `ok: false` with a code.
 */
import { ClientProblemCode, RefusalCode, type ProblemCode } from '../results';
import type { MachineSpec, TypedMachine } from '../typed';
import { createSnapshotClient, type AdvanceOptions, type SnapshotExecutor, type SnapshotOutput } from './transport';

/**
 * Total: the stored snapshot (JSON, or null when there is no draft yet), or why not. `revision` is the instance's
 * revision beside the snapshot when the server reports one (the typed `machines` root does), so a client that also
 * hears pushes can drop anything at or below the revision it holds.
 */
export type DraftResult =
  | { ok: true; snapshot: string | null; revision?: number | null }
  | { ok: false; code: ProblemCode; message: string };

/** The code reported when the request never reached the server (offline, DNS, a 500, an auth error). */
export const TRANSPORT_ERROR = ClientProblemCode.TransportError;

/**
 * User-facing fallbacks, used only when the server refuses WITHOUT a message or the transport fails.
 * A server-supplied message always wins — it is the specific one. Override these with product copy
 * ("The letter could not be sent"); the defaults are deliberately generic because the engine has no
 * idea what the flow is called.
 */
export interface DraftMessages {
  saveRefused: string;
  saveUnavailable: string;
  advanceRefused: string;
  advanceUnavailable: string;
  loadUnavailable: string;
  sendRefused: string;
  sendUnavailable: string;
  startRefused: string;
  startUnavailable: string;
  discardRefused: string;
  discardUnavailable: string;
}

const DEFAULT_MESSAGES: DraftMessages = {
  saveRefused: 'Your draft could not be saved.',
  saveUnavailable: 'Could not save your draft. Check your connection.',
  advanceRefused: 'That step could not be completed.',
  advanceUnavailable: 'Could not reach the server. Check your connection.',
  loadUnavailable: 'Could not load your draft. Check your connection.',
  sendRefused: 'The action could not be completed.',
  sendUnavailable: 'The action could not be completed. Please try again.',
  startRefused: 'A new draft could not be started.',
  startUnavailable: 'Could not start a new draft. Check your connection.',
  discardRefused: 'The draft could not be discarded.',
  discardUnavailable: 'Could not discard the draft. Check your connection.',
};

export interface DraftSessionOptions {
  /** The machine's executor, from `createMachineExecutor`: every operation is sent by its persisted id. */
  executor: SnapshotExecutor;
  /**
   * The machine this draft belongs to: a {@link TypedMachine} (its `id` and `schemaHash` are read from
   * it, which is what you want — they cannot then disagree with the machine actually running) or the
   * raw name, with `schemaHash` passed alongside.
   */
  machine: TypedMachine<MachineSpec> | string;
  /**
   * The draft id, scoped to the authenticated user. Drafts are keyed `(user_key, id)` server-side with
   * `machine` NOT part of the key, so each machine needs one id distinct from every other machine's.
   */
  id: string;
  /** Only when `machine` is a raw name. Omit to opt out of the server's version-skew check. */
  schemaHash?: string;
  messages?: Partial<DraftMessages>;
}

export interface DraftSession {
  /** The machine name every request carries. */
  readonly machine: string;
  /** The draft id every request carries. */
  readonly id: string;
  /** Soft autosave: validate + store the snapshot. Refused against a COMMITTED draft. */
  save(snapshot: string): Promise<DraftResult>;
  /** Authoritative transition: the server fires the trigger and stores the result. */
  advance(trigger: string, options?: Omit<AdvanceOptions, 'schemaHash'>): Promise<DraftResult>;
  /** Resume: the stored snapshot, or `{ ok: true, snapshot: null }` when there is no draft yet. */
  load(): Promise<DraftResult>;
  /** Run the bound effect exactly once. A retry with the same `requestId` replays the same receipt. */
  send(requestId?: string): Promise<DraftResult>;
  /** Delete the draft. Idempotent: a draft already gone is `{ ok: true, snapshot: null }`. */
  discard(requestId: string): Promise<DraftResult>;
}

/** Total: the new draft's server-minted id and its first snapshot, or why not. */
export type StartResult =
  | { ok: true; id: string; snapshot: string; created: boolean; revision?: number | null }
  | { ok: false; code: ProblemCode; message: string };

export interface StartDraftOptions {
  /** The machine's executor, from `createMachineExecutor`. */
  executor: SnapshotExecutor;
  /** The machine to start, as in {@link DraftSessionOptions.machine}. */
  machine: TypedMachine<MachineSpec> | string;
  /**
   * The start's idempotency key. Keep it until the start has answered: a retry with the same key returns the
   * draft the first request created rather than a second one.
   */
  requestId: string;
  /** The start input as JSON, when the machine's start takes one. */
  input?: string;
  /** Only when `machine` is a raw name. Omit to opt out of the server's version-skew check. */
  schemaHash?: string;
  messages?: Partial<DraftMessages>;
}

const nameOf = (machine: TypedMachine<MachineSpec> | string) =>
  typeof machine === 'string' ? machine : machine.id;

const hashOf = (machine: TypedMachine<MachineSpec> | string, schemaHash: string | undefined) =>
  typeof machine === 'string' ? schemaHash : machine.schemaHash;

// The one place a transport throw or a typed problem becomes a result, so no caller writes a try/catch.
const settleOutput = async (
  call: () => Promise<SnapshotOutput>,
  refused: string,
  unavailable: string,
): Promise<{ ok: true; output: SnapshotOutput } | { ok: false; code: ProblemCode; message: string }> => {
  let output: SnapshotOutput;
  try {
    output = await call();
  } catch {
    return { ok: false, code: TRANSPORT_ERROR, message: unavailable };
  }
  if (output.problem) {
    return { ok: false, code: output.problem.code, message: output.problem.message || refused };
  }
  return { ok: true, output };
};

/**
 * Creates a draft from the machine's declared start: the server mints its id and builds its first snapshot,
 * so the client never writes one. Total, like every {@link DraftSession} method; bind the returned id with
 * {@link createDraftSession} to move the draft on.
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#talking-to-a-server
 */
export async function startDraft(options: StartDraftOptions): Promise<StartResult> {
  const machine = nameOf(options.machine);
  const schemaHash = hashOf(options.machine, options.schemaHash);
  const messages: DraftMessages = { ...DEFAULT_MESSAGES, ...options.messages };
  const settled = await settleOutput(
    () =>
      createSnapshotClient(options.executor).start(machine, options.requestId, { input: options.input, schemaHash }),
    messages.startRefused,
    messages.startUnavailable,
  );
  if (!settled.ok) return settled;
  const { id, snapshot, created, revision } = settled.output;
  if (!id || !snapshot)
    return { ok: false, code: TRANSPORT_ERROR, message: messages.startUnavailable };
  return { ok: true, id, snapshot, created: created ?? true, ...(revision != null ? { revision } : {}) };
}

/**
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#talking-to-a-server
 */
export function createDraftSession(options: DraftSessionOptions): DraftSession {
  const { id } = options;
  const client = createSnapshotClient(options.executor);
  const machine = nameOf(options.machine);
  const schemaHash = hashOf(options.machine, options.schemaHash);
  const messages: DraftMessages = { ...DEFAULT_MESSAGES, ...options.messages };

  const settle = async (
    call: () => Promise<SnapshotOutput>,
    refused: string,
    unavailable: string,
  ): Promise<DraftResult> => {
    const settled = await settleOutput(call, refused, unavailable);
    return settled.ok
      ? {
          ok: true,
          snapshot: settled.output.snapshot,
          ...(settled.output.revision != null ? { revision: settled.output.revision } : {}),
        }
      : settled;
  };

  return {
    machine,
    id,

    save: (snapshot) =>
      settle(
        () => client.save(machine, id, snapshot, schemaHash),
        messages.saveRefused,
        messages.saveUnavailable,
      ),

    advance: (trigger, advanceOptions = {}) =>
      settle(
        () => client.advance(machine, id, trigger, { ...advanceOptions, schemaHash }),
        messages.advanceRefused,
        messages.advanceUnavailable,
      ),

    // The server answers a draft that does not exist yet with a `NOT_FOUND` problem; to a caller
    // that is no draft, as this method's contract says, not a failure.
    load: async () => {
      const loaded = await settle(
        () => client.load(machine, id, schemaHash),
        messages.loadUnavailable,
        messages.loadUnavailable,
      );
      return !loaded.ok && loaded.code === RefusalCode.NotFound ? { ok: true, snapshot: null } : loaded;
    },

    send: (requestId) =>
      settle(
        () => client.send(machine, id, requestId, schemaHash),
        messages.sendRefused,
        messages.sendUnavailable,
      ),

    discard: (requestId) =>
      settle(
        () => client.discard(machine, id, requestId),
        messages.discardRefused,
        messages.discardUnavailable,
      ),
  };
}
