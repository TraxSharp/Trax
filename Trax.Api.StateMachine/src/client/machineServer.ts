/**
 * A {@link MachineServer} over a host's typed `machines` root and its `machineChanged` subscription: what
 * `ServerMachineController` and `useServerMachine` drive one instance through.
 *
 * Every operation is sent by the persisted id the host's manifest lists for it, with its values as variables, through
 * the same executor as {@link createMachineExecutor}; the change stream is subscribed by its persisted id over a
 * graphql-ws socket the page owns. The package does not depend on graphql-ws: any socket with its `subscribe` shape
 * will do, and {@link persistedMessageReplacer} is the `jsonMessageReplacer` that lets it send a persisted id.
 */
import type { MachineSpec, TypedMachine } from '../typed';
import type {
  EventResponse,
  GetResponse,
  MachineServer,
  ServerProblem,
  ServerSnapshot,
  StartResponse,
  StreamMessage,
} from '../server/serverController';
import { createMachineExecutor, withoutNulls, type MachineDocument } from './machines';
import { createSnapshotClient, type HttpExecutorOptions, type SnapshotOutput } from './transport';

/** What a subscription delivers: one GraphQL result per event, as graphql-ws hands it to a sink. */
export interface SubscriptionSink {
  next(value: { data?: unknown; errors?: readonly { message: string; extensions?: Record<string, unknown> }[] }): void;
  error(error: unknown): void;
  complete(): void;
}

/**
 * The part of a graphql-ws `Client` this needs: `subscribe(payload, sink)`, returning the function that completes the
 * subscription. A graphql-ws client is one; create it with `jsonMessageReplacer: persistedMessageReplacer`.
 */
export interface PersistedSubscriptionSocket {
  subscribe(payload: { query: string; id: string; variables: Record<string, unknown> }, sink: SubscriptionSink): () => void;
}

export interface MachineServerOptions extends HttpExecutorOptions {
  /** The machine the server drives; its id names its field under `machines`. */
  machine: TypedMachine<MachineSpec> | { id: string; version: number };
  /**
   * The machine's persisted documents, from the host's manifest (`operations { machineDocuments { ... } }`),
   * `machineChanged` included. Each must be of this machine and of the IR its twin was generated from.
   */
  documents: readonly MachineDocument[];
  /** The socket the change stream is subscribed over. */
  socket: PersistedSubscriptionSocket;
  /**
   * Delays, in milliseconds, before subscribing again after a stream that ended without saying why (its host stopped,
   * or the socket gave up). The last delay repeats for as long as the stream is wanted.
   */
  resubscribeDelays?: readonly number[];
}

/** The answer to `discard`. A discard of an instance already gone, or never the caller's, is `discarded` too. */
export type DiscardResponse = { kind: 'discarded' } | ServerProblem;

/** A {@link MachineServer} that can also discard an instance. */
export interface TypedMachineServer extends MachineServer {
  discard(id: string, requestId: string): Promise<DiscardResponse>;
}

/** The default delays before subscribing again after a stream ended without a reason. */
export const DEFAULT_RESUBSCRIBE_DELAYS: readonly number[] = [1000, 2000, 5000];

/**
 * Leaves an empty `query` out of a graphql-ws message. graphql-ws requires a subscribe message to carry a document,
 * and a host that runs persisted documents refuses one beside an id, so the stream is subscribed with an empty one
 * and this replacer drops it. Pass it as the client's `jsonMessageReplacer`.
 */
export function persistedMessageReplacer(key: string, value: unknown): unknown {
  return key === 'query' && value === '' ? undefined : value;
}

const MACHINE_CHANGED = 'machineChanged';
const CLIENT_OUTDATED = 'client-outdated';
const NOT_FOUND = 'not-found';

interface MachineChange {
  rowId?: number | string | null;
  revision?: number | null;
  state?: string | null;
  context?: Record<string, unknown> | null;
  dueAt?: string | null;
  gone?: boolean | null;
  reload?: boolean | null;
  problem?: { code: string; message: string } | null;
}

const problemOf = (problem: { code: string; message: string }): ServerProblem => ({
  kind: 'problem',
  code: problem.code,
  message: problem.message,
});

// The executor's answer, which carries the snapshot as the twin's JSON, as the controller's server snapshot.
const snapshotOf = (output: SnapshotOutput): ServerSnapshot => {
  const parsed = JSON.parse(output.snapshot!) as { state: string; context: Record<string, unknown> };
  // A due time the answer does not carry, or says it cannot know, stays absent, so the controller tells it from null
  // (no timer, or the server cleared it).
  return {
    revision: output.revision ?? 0,
    state: parsed.state,
    context: parsed.context,
    ...(output.dueAt === undefined ? {} : { dueAt: output.dueAt }),
  };
};

const answered = (output: SnapshotOutput): { kind: 'ok'; snapshot: ServerSnapshot } | ServerProblem => {
  if (output.problem) return problemOf(output.problem);
  if (output.snapshot == null) throw new Error('The server answered without a snapshot or a problem.');
  return { kind: 'ok', snapshot: snapshotOf(output) };
};

/**
 * Builds the {@link MachineServer} for one exposed machine. A refusal resolves as a problem; a GraphQL error other
 * than `client-outdated` or `server-outdated`, or a failed request, rejects, which the controller reports as a
 * transport failure.
 *
 * The stream maps each `machineChanged` event onto the controller's messages: a snapshot (with its incarnation as
 * `rowId`), `gone` for a deleted or expired instance and for one the server will not let the caller watch (the answer
 * it gives a missing id), `client-outdated`, and `reconnected` when the server asks for a reload. A stream that ends
 * any other way is subscribed again after {@link MachineServerOptions.resubscribeDelays}; the new stream's first
 * event is the instance as it is then, so nothing written meanwhile is lost and nothing is read again.
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#talking-to-a-server
 */
export function createMachineServer(endpoint: string, options: MachineServerOptions): TypedMachineServer {
  const { machine, documents, socket } = options;
  const machineChanged = documents.find((d) => d.operation === MACHINE_CHANGED);
  if (!machineChanged)
    throw new Error(
      `The documents of '${machine.id}' have no ${MACHINE_CHANGED} document. Export them again from the host's manifest.`,
    );
  const delays = options.resubscribeDelays?.length ? options.resubscribeDelays : DEFAULT_RESUBSCRIBE_DELAYS;
  const client = createSnapshotClient(
    createMachineExecutor(endpoint, {
      fetch: options.fetch,
      headers: options.headers,
      machines: [{ machine, documents }],
    }),
  );

  const start = async (input: unknown, requestId: string): Promise<StartResponse> => {
    const output = await client.start(machine.id, requestId, {
      input: input === undefined ? undefined : JSON.stringify(input),
    });
    const result = answered(output);
    if (result.kind === 'problem') return result;
    if (!output.id) throw new Error('The server started an instance without answering its id.');
    return { kind: 'started', id: output.id, snapshot: result.snapshot };
  };

  const event: MachineServer['event'] = async (request): Promise<EventResponse> =>
    answered(
      await client.advance(machine.id, request.id, request.trigger, {
        input: request.input === undefined ? undefined : JSON.stringify(request.input),
        requestId: request.requestId,
        clientResult: request.clientResult,
      }),
    );

  const get = async (id: string): Promise<GetResponse> => answered(await client.load(machine.id, id));

  const discard = async (id: string, requestId: string): Promise<DiscardResponse> => {
    const output = await client.discard(machine.id, id, requestId);
    if (output.problem) return problemOf(output.problem);
    return { kind: 'discarded' };
  };

  const subscribe = (id: string, onMessage: (message: StreamMessage) => void): (() => void) => {
    let wanted = true;
    let attempt = 0;
    let unsubscribe: (() => void) | null = null;
    let timer: ReturnType<typeof setTimeout> | null = null;

    const open = () => {
      timer = null;
      if (!wanted) return;
      // Whether this stream ended for good (the instance is gone, or this client is outdated), or delivered anything.
      let ended = false;
      const deliver = (message: StreamMessage) => {
        if (!wanted || ended) return;
        if (message.type === 'gone' || message.type === 'client-outdated') ended = true;
        onMessage(message);
      };
      const again = () => {
        if (!wanted || ended) return;
        ended = true;
        const close = unsubscribe;
        unsubscribe = null;
        close?.();
        const delay = delays[Math.min(attempt, delays.length - 1)];
        attempt++;
        timer = setTimeout(open, delay);
      };

      unsubscribe = socket.subscribe(
        { query: '', id: machineChanged.id, variables: { id } },
        {
          next: (value) => {
            const error = value.errors?.[0];
            if (error) {
              if (error.extensions?.code === CLIENT_OUTDATED) deliver({ type: 'client-outdated' });
              else again();
              return;
            }
            const change = (value.data as { machineChanged?: MachineChange } | null | undefined)?.machineChanged;
            if (!change) return;
            if (change.problem) {
              if (change.problem.code === CLIENT_OUTDATED) deliver({ type: 'client-outdated' });
              else if (change.problem.code === NOT_FOUND) deliver({ type: 'gone' });
              else again();
              return;
            }
            if (change.gone) return deliver({ type: 'gone' });
            if (change.reload) return deliver({ type: 'reconnected' });
            if (change.state == null || change.revision == null) return;
            attempt = 0;
            deliver({
              type: 'snapshot',
              snapshot: {
                rowId: change.rowId == null ? null : String(change.rowId),
                revision: change.revision,
                state: change.state,
                context: withoutNulls(change.context ?? {}) as Record<string, unknown>,
                dueAt: change.dueAt,
              },
            });
          },
          error: again,
          complete: again,
        },
      );
    };

    open();
    return () => {
      wanted = false;
      if (timer !== null) clearTimeout(timer);
      unsubscribe?.();
    };
  };

  return { start, event, get, discard, subscribe };
}
