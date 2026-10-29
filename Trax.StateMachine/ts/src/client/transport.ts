import type { ProblemCode } from '../results';
/**
 * The client half of "one set of operations, any machine".
 *
 * `Trax.StateMachine.Persistence` serves six generic mutations, when the host opts in to them —
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
 * onto its own document, and an app with none can use `createMachineExecutor`, which sends the typed `machines`
 * root's persisted documents.
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
  /**
   * Accepts an absolute `http:` endpoint on a host that is not loopback. Off by default: the headers that authenticate
   * each request would travel in cleartext. A relative endpoint is the page's own origin and is always accepted.
   */
  allowInsecureTransport?: boolean;
  /**
   * The largest response body the host may answer with, in bytes; 16 MiB by default. A larger one fails the request,
   * which the session reports as a transport error, and is not read past the limit.
   */
  maxResponseSize?: number;
  /**
   * How long one request may take, in milliseconds, from sending it to the last byte of its answer; 30 seconds by
   * default. A request past it is aborted and fails as a transport error, which `retry` resends under the same request
   * id. `Infinity` waits for as long as the host takes.
   */
  requestTimeout?: number;
}

/** The response body limit when none is set: 16 MiB. */
export const DEFAULT_MAX_RESPONSE_SIZE = 16 * 1024 * 1024;

/** The request timeout when none is set: 30 seconds. */
export const DEFAULT_REQUEST_TIMEOUT = 30_000;

/**
 * Runs one request under `timeout` milliseconds: past it, the signal `run` was given is aborted and the promise rejects,
 * whether or not the `fetch` heeds the signal.
 */
export async function withRequestTimeout<T>(timeout: number, run: (signal: AbortSignal) => Promise<T>): Promise<T> {
  const controller = new AbortController();
  if (!Number.isFinite(timeout)) return run(controller.signal);
  let timer: ReturnType<typeof setTimeout> | undefined;
  const expired = new Promise<never>((_, reject) => {
    timer = setTimeout(() => {
      controller.abort();
      reject(new Error(`The server did not answer within ${timeout} ms.`));
    }, Math.max(0, timeout));
  });
  try {
    return await Promise.race([run(controller.signal), expired]);
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Throws unless `response` is the endpoint's own answer: a redirect is never followed (the request is sent with
 * `redirect: 'error'`), and a `fetch` that followed one anyway, or handed back the redirect itself, fails the request
 * rather than reading an answer from another host.
 */
export function requireNoRedirect(response: Response): void {
  if (response.redirected || response.type === 'opaqueredirect' || (response.status >= 300 && response.status < 400))
    throw new Error(`The server answered with a redirect (${response.status}), which is not followed.`);
}

/**
 * The response body as text, read no further than `max` bytes. Throws once it passes the limit, whether or not the
 * host said its length up front, and stops reading the body. A response with no body stream to read (an XHR polyfill,
 * React Native) is read whole only when it says a length within the limit, and refused otherwise; a Node stream is
 * read by async iteration.
 */
export async function readBounded(response: Response, max: number): Promise<string> {
  const tooLarge = () => new Error(`The server answered with a body larger than ${max} bytes.`);
  const header = response.headers?.get('content-length');
  const declared = header == null || header.trim() === '' ? NaN : Number(header);
  if (Number.isFinite(declared) && declared > max) {
    await cancel(response.body);
    throw tooLarge();
  }
  const body = response.body as unknown;
  if (!body) {
    if (!Number.isFinite(declared) || declared < 0)
      throw new Error('The server answered without a length, and the response has no body stream to read it bounded.');
    const text = await response.text();
    if (new TextEncoder().encode(text).length > max) throw tooLarge();
    return text;
  }
  const chunks: Uint8Array[] = [];
  let total = 0;
  const take = (value: Uint8Array): boolean => {
    total += value.byteLength;
    if (total > max) return false;
    chunks.push(value);
    return true;
  };
  if (typeof (body as ReadableStream<Uint8Array>).getReader === 'function') {
    const reader = (body as ReadableStream<Uint8Array>).getReader();
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      if (!take(value)) {
        await reader.cancel().catch(() => undefined);
        throw tooLarge();
      }
    }
  } else if (typeof (body as AsyncIterable<Uint8Array>)[Symbol.asyncIterator] === 'function') {
    // Leaving the loop early returns the iterator, which destroys a Node stream.
    for await (const value of body as AsyncIterable<Uint8Array | string>) {
      if (!take(typeof value === 'string' ? new TextEncoder().encode(value) : value)) throw tooLarge();
    }
  } else {
    throw new Error('The response body is not a stream this client can read.');
  }
  const bytes = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder().decode(bytes);
}

const cancel = async (body: unknown): Promise<void> => {
  const stream = body as { cancel?: () => Promise<void>; destroy?: () => void } | null;
  try {
    if (typeof stream?.cancel === 'function') await stream.cancel();
    else stream?.destroy?.();
  } catch {
    // The body is abandoned either way.
  }
};

/**
 * Throws unless `endpoint` may carry credentials: an absolute one must be https unless its host is this machine
 * (localhost, 127.0.0.0/8, ::1) or `allowInsecure` is set. A relative one goes to the page's own origin, whatever its
 * scheme, except a scheme-relative one (`//host/...`, or `\\host\...`, which a browser reads the same way), which goes
 * to the host it names and is refused unless `allowInsecure` is set. Every machine executor and server runs it; the
 * generated client runs it before each train request too.
 */
export function requireSecureEndpoint(endpoint: string, allowInsecure: boolean | undefined): void {
  if (allowInsecure) return;
  let url: URL;
  try {
    url = new URL(endpoint);
  } catch {
    if (isSchemeRelative(endpoint))
      throw new Error(
        'The endpoint is scheme-relative, so it goes to another host than the page\'s own. Give an absolute https:// endpoint, a path on this origin, or set allowInsecureTransport when the network is trusted.',
      );
    return;
  }
  if (url.protocol === 'https:' || isLoopback(url.hostname)) return;
  throw new Error(
    `The endpoint ${url.protocol}//${url.host} is not over TLS, so whatever authenticates the client would travel in cleartext. Use https://, a loopback host, or set allowInsecureTransport when the network is trusted.`,
  );
}

// Whether a relative URL names a host of its own. A URL parser drops every tab and newline and the spaces and control
// characters around it, and reads a backslash as a slash in an http or https URL, so `/\t/host` and `\\host` are
// `//host` too.
const isSchemeRelative = (endpoint: string): boolean =>
  /^[\\/]{2}/.test(endpoint.replace(/[\t\n\r]/g, '').replace(/^[\u0000-\u0020]+/, ''));

const isLoopback = (hostname: string): boolean =>
  hostname === 'localhost' || hostname === '[::1]' || /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(hostname);

