/**
 * One user's one draft of one machine, over the four generic operations.
 *
 * `createSnapshotClient` is machine-agnostic and takes `(machine, id)` on every call; a UI almost always
 * wants the opposite — a handle that has already been told which machine and which draft it is looking
 * at, so the wizard code never repeats either. That is a {@link DraftSession}.
 *
 * The other thing it adds is TOTALITY at the edge. The server answers a refusal as data (a typed
 * `problem`) but the network can still fail, so a caller that wanted to know "did my draft save?" would
 * otherwise need a try/catch at every call site. Every method here returns a {@link DraftResult}
 * instead: nothing throws, and an unreachable server is just another `ok: false` with a code.
 */
import type { MachineSpec, TypedMachine } from '../typed';
import type { AdvanceOptions, SnapshotClient, SnapshotOutput } from './transport';

/** Total: the stored snapshot (JSON, or null when there is no draft yet), or why not. */
export type DraftResult =
  | { ok: true; snapshot: string | null }
  | { ok: false; code: string; message: string };

/** The code reported when the request never reached the server (offline, DNS, a 500, an auth error). */
export const TRANSPORT_ERROR = 'transport-error';

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
}

const DEFAULT_MESSAGES: DraftMessages = {
  saveRefused: 'Your draft could not be saved.',
  saveUnavailable: 'Could not save your draft. Check your connection.',
  advanceRefused: 'That step could not be completed.',
  advanceUnavailable: 'Could not reach the server. Check your connection.',
  loadUnavailable: 'Could not load your draft. Check your connection.',
  sendRefused: 'The action could not be completed.',
  sendUnavailable: 'The action could not be completed. Please try again.',
};

export interface DraftSessionOptions {
  client: SnapshotClient;
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
}

export function createDraftSession(options: DraftSessionOptions): DraftSession {
  const { client, id } = options;
  const machine = typeof options.machine === 'string' ? options.machine : options.machine.id;
  const schemaHash =
    typeof options.machine === 'string' ? options.schemaHash : options.machine.schemaHash;
  const messages: DraftMessages = { ...DEFAULT_MESSAGES, ...options.messages };

  // The one place a transport throw or a typed problem becomes a DraftResult, so no method below —
  // and no caller above — writes a try/catch.
  const settle = async (
    call: () => Promise<SnapshotOutput>,
    refused: string,
    unavailable: string,
  ): Promise<DraftResult> => {
    let output: SnapshotOutput;
    try {
      output = await call();
    } catch {
      return { ok: false, code: TRANSPORT_ERROR, message: unavailable };
    }
    if (output.problem) {
      return {
        ok: false,
        code: output.problem.code,
        message: output.problem.message || refused,
      };
    }
    return { ok: true, snapshot: output.snapshot };
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

    load: () =>
      settle(
        () => client.load(machine, id, schemaHash),
        messages.loadUnavailable,
        messages.loadUnavailable,
      ),

    send: (requestId) =>
      settle(
        () => client.send(machine, id, requestId, schemaHash),
        messages.sendRefused,
        messages.sendUnavailable,
      ),
  };
}
