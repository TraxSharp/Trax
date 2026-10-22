import {
  createContext,
  createElement,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
  type ReactNode,
} from "react";
import {
  TraxOperationError,
  TraxTransportError,
  type TraxAnswer,
  type TraxDocument,
  type TraxTransport,
} from "../trax/index.g";
import { setActiveCount } from "./activity";
import { noteQueryResult } from "./refreshStatus";

// The console's React side of the generated operations client (src/trax). Every request goes through the client's
// documents and the transport it was given; this file adds what a page needs on top: a cache shared by every page
// under one provider, hooks that read it, one stream per document and variables however many pages follow it, and
// the count of requests in flight that the header's progress bar reads.
//
// A read is cache-and-network unless it asks otherwise: a page renders what an earlier page read at once, then the
// answer. Operations mutations answer with a generic OperationResponse, so a write cannot say which reads it made
// stale; pages refetch on the data-changed stream and on their own writes instead.

/** How a read uses the cache: show what it holds and read again, or show only this read's answer. */
export type RequestPolicy = "cache-and-network" | "network-only";

/** What a read or write answered: its data, its error, or both when the host answered part of it. */
export interface OperationResult<R> {
  data?: R;
  error?: Error;
}

/** A read's state as a page renders it. `stale` is true while a cached answer is read again. */
export interface QueryState<R> extends OperationResult<R> {
  fetching: boolean;
  stale: boolean;
}

/** A write's state: `fetching` while it is in flight. */
export interface MutationState<R> extends OperationResult<R> {
  fetching: boolean;
}

/** A stream's state: `data` is the latest frame, or what the reducer made of the frames so far. */
export interface SubscriptionState<R> {
  data?: R;
  error?: Error;
  fetching: boolean;
}

/** Options every read takes. */
export interface ReadOptions {
  /** The answer is not a refresh: its failure does not mark the header's refresh failed (a capability probe). */
  ignoreRefreshStatus?: boolean;
}

/** The variables a document takes, required in the options only when one of them is required. */
type VariablesOption<V> = Record<string, never> extends V ? { variables?: V } : { variables: V };

export type QueryOptions<V> = VariablesOption<V> & ReadOptions & { requestPolicy?: RequestPolicy; pause?: boolean };

export type SubscriptionOptions<V> = VariablesOption<V> & { pause?: boolean };

interface Entry {
  data?: unknown;
  error?: Error;
  fetching: boolean;
  /** How many answers this entry has had: a network-only read shows the entry only after one it asked for. */
  answers: number;
  /** The newest request sent for the entry: an older one's answer is dropped. */
  latest: number;
  inflight: Promise<OperationResult<unknown>> | null;
  listeners: Set<() => void>;
  /** A fresh object per change, so useSyncExternalStore sees the change. */
  snapshot: Snapshot;
}

interface Snapshot {
  data?: unknown;
  error?: Error;
  fetching: boolean;
  answers: number;
}

interface Stream {
  listeners: Set<(answer: TraxAnswer) => void>;
  errors: Set<(error: Error) => void>;
  stop: () => void;
}

/** The error a page shows for an answer: its GraphQL errors, or why no answer arrived. */
export function errorOf(answer: TraxAnswer): Error | undefined {
  return answer.errors?.length ? new TraxOperationError(answer.errors) : undefined;
}

function asError(error: unknown): Error {
  if (error instanceof Error) return error;
  return new TraxTransportError(typeof error === "string" ? error : "The request failed.");
}

// Variables as a key: the same values in any key order are one key, and an undefined value is no value.
function stable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([, v]) => v !== undefined)
        .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
        .map(([k, v]) => [k, stable(v)]),
    );
  }
  return value;
}

function keyOf(document: TraxDocument<unknown, unknown>, variables: unknown): string {
  return `${document.name}:${JSON.stringify(stable(variables ?? {}))}`;
}

/**
 * The console's client: one per provider (the app, a test, a story), so each has its own cache and its own transport.
 * Requests go through the transport the generated client defines; the documents are the generated ones.
 */
export class ConsoleClient {
  readonly transport: TraxTransport;
  private readonly entries = new Map<string, Entry>();
  private readonly streams = new Map<string, Stream>();
  private inflight = 0;
  private sequence = 0;
  private answered: ReadonlySet<string> | null = null;

  constructor(transport: TraxTransport) {
    this.transport = transport;
  }

  /** Limits the reads this client says it answers (the demo answers only what it recorded). */
  limitAnswers(operations: Iterable<string>): void {
    this.answered = new Set(operations);
  }

  /** Whether this client answers the query named `operation`. A host and the mock answer every one. */
  answers(operation: string): boolean {
    return this.answered?.has(operation) ?? true;
  }

  /**
   * Sends a query, always to the host, and keeps its answer for the pages that read it. Never throws: an error is in
   * the result.
   */
  query<V, R>(document: TraxDocument<V, R>, variables?: V, options: ReadOptions = {}): Promise<OperationResult<R>> {
    const entry = this.entry(keyOf(document, variables));
    return this.read(entry, document, variables, options, true) as Promise<OperationResult<R>>;
  }

  /** Sends a mutation. Never throws: an error is in the result. */
  async mutation<V, R>(document: TraxDocument<V, R>, variables?: V): Promise<OperationResult<R>> {
    this.begin();
    try {
      const answer = await this.transport.execute({ document, variables: (stable(variables ?? {}) as Record<string, unknown>) });
      return { data: (answer.data ?? undefined) as R | undefined, error: errorOf(answer) };
    } catch (error) {
      return { error: asError(error) };
    } finally {
      this.end();
    }
  }

  /**
   * Follows a subscription. Pages following the same document with the same variables share one stream, which stops
   * when the last of them stops.
   */
  subscribe<V, R>(
    document: TraxDocument<V, R>,
    variables: V | undefined,
    next: (data: R) => void,
    error: (error: Error) => void,
  ): () => void {
    const key = keyOf(document, variables);
    let stream = this.streams.get(key);
    const onAnswer = (answer: TraxAnswer) => {
      const failure = errorOf(answer);
      if (failure) error(failure);
      else next(answer.data as R);
    };
    if (!stream) {
      const created: Stream = { listeners: new Set(), errors: new Set(), stop: () => {} };
      this.streams.set(key, created);
      created.listeners.add(onAnswer);
      created.errors.add(error);
      created.stop = this.transport.subscribe(
        { document, variables: stable(variables ?? {}) as Record<string, unknown> },
        {
          next: (answer) => created.listeners.forEach((l) => l(answer)),
          error: (e) => {
            if (this.streams.get(key) === created) this.streams.delete(key);
            created.errors.forEach((l) => l(asError(e)));
          },
          complete: () => {
            if (this.streams.get(key) === created) this.streams.delete(key);
          },
        },
      );
      stream = created;
    } else {
      stream.listeners.add(onAnswer);
      stream.errors.add(error);
    }
    const joined = stream;
    return () => {
      joined.listeners.delete(onAnswer);
      joined.errors.delete(error);
      if (joined.listeners.size === 0) {
        if (this.streams.get(key) === joined) this.streams.delete(key);
        joined.stop();
      }
    };
  }

  /** @internal The cache entry of a document and its variables. */
  entry(key: string): Entry {
    let entry = this.entries.get(key);
    if (!entry) {
      entry = {
        fetching: false,
        answers: 0,
        latest: 0,
        inflight: null,
        listeners: new Set(),
        snapshot: { fetching: false, answers: 0 },
      };
      this.entries.set(key, entry);
    }
    return entry;
  }

  /**
   * @internal Reads an entry. `fresh` sends a new request even when one is in flight (a refetch after a write must not
   * take an answer read before it); otherwise an in-flight request is joined, so pages mounting together send one.
   */
  read(
    entry: Entry,
    document: TraxDocument<unknown, unknown>,
    variables: unknown,
    options: ReadOptions,
    fresh: boolean,
  ): Promise<OperationResult<unknown>> {
    if (!fresh && entry.inflight) return entry.inflight;
    const request = ++this.sequence;
    entry.latest = request;
    entry.fetching = true;
    this.publish(entry);
    this.begin();
    const sent = (async (): Promise<OperationResult<unknown>> => {
      let result: OperationResult<unknown>;
      try {
        const answer = await this.transport.execute({ document, variables: stable(variables ?? {}) as Record<string, unknown> });
        result = { data: answer.data ?? undefined, error: errorOf(answer) };
      } catch (error) {
        result = { error: asError(error) };
      } finally {
        this.end();
      }
      if (!options.ignoreRefreshStatus) noteQueryResult(result.error ? result.error.message : null);
      if (entry.latest === request) {
        entry.data = result.data ?? (result.error ? entry.data : undefined);
        entry.error = result.error;
        entry.fetching = false;
        entry.answers += 1;
        entry.inflight = null;
        this.publish(entry);
      }
      return result;
    })();
    entry.inflight = sent;
    return sent;
  }

  private publish(entry: Entry): void {
    entry.snapshot = { data: entry.data, error: entry.error, fetching: entry.fetching, answers: entry.answers };
    entry.listeners.forEach((l) => l());
  }

  private begin(): void {
    this.inflight += 1;
    setActiveCount(this.inflight);
  }

  private end(): void {
    this.inflight = Math.max(0, this.inflight - 1);
    setActiveCount(this.inflight);
  }
}

const Context = createContext<ConsoleClient | null>(null);

/** Gives the pages under it a client. */
export function ConsoleClientProvider({ client, children }: { client: ConsoleClient; children?: ReactNode }) {
  return createElement(Context.Provider, { value: client }, children);
}

/** The client of the nearest provider. */
export function useConsoleClient(): ConsoleClient {
  const client = useContext(Context);
  if (!client) throw new Error("useConsoleClient needs a ConsoleClientProvider above it.");
  return client;
}

const idle: Snapshot = { fetching: false, answers: 0 };

/**
 * Reads a document, and again when its variables change. Returns its state and a function that reads it again from
 * the host; until that answer arrives the page keeps what it had, marked `stale`.
 */
export function useTraxQuery<V, R>(
  document: TraxDocument<V, R>,
  ...[options]: Record<string, never> extends V ? [options?: QueryOptions<V>] : [options: QueryOptions<V>]
): [QueryState<R>, () => void] {
  const client = useConsoleClient();
  const variables = (options as { variables?: V } | undefined)?.variables;
  const key = keyOf(document, variables);
  const pause = options?.pause ?? false;
  const policy = options?.requestPolicy ?? "cache-and-network";
  const ignoreRefreshStatus = options?.ignoreRefreshStatus ?? false;
  const entry = client.entry(key);

  // The answers the entry had when this read began: a network-only read shows only answers after it.
  const [since, setSince] = useState<{ key: string; client: ConsoleClient; answers: number }>(() => ({
    key,
    client,
    answers: policy === "network-only" ? entry.answers : 0,
  }));
  let floor = since.answers;
  if (since.key !== key || since.client !== client) {
    floor = policy === "network-only" ? entry.answers : 0;
    setSince({ key, client, answers: floor });
  }

  const subscribe = useCallback(
    (listener: () => void) => {
      entry.listeners.add(listener);
      return () => {
        entry.listeners.delete(listener);
      };
    },
    [entry],
  );
  const snapshot = useSyncExternalStore(subscribe, () => entry.snapshot, () => idle);

  // The latest arguments, for the effect and the refetch, without making them re-run on every render.
  const latest = useRef({ document, variables, ignoreRefreshStatus });
  useEffect(() => {
    latest.current = { document, variables, ignoreRefreshStatus };
  });

  useEffect(() => {
    if (pause) return;
    const { document: d, variables: v, ignoreRefreshStatus: ignore } = latest.current;
    void client.read(entry, d as TraxDocument<unknown, unknown>, v, { ignoreRefreshStatus: ignore }, policy === "network-only");
  }, [client, entry, key, pause, policy]);

  const refetch = useCallback(() => {
    const { document: d, variables: v, ignoreRefreshStatus: ignore } = latest.current;
    void client.read(entry, d as TraxDocument<unknown, unknown>, v, { ignoreRefreshStatus: ignore }, true);
  }, [client, entry]);

  const shown = snapshot.answers > floor;
  const state = useMemo<QueryState<R>>(
    () => ({
      data: shown ? (snapshot.data as R | undefined) : undefined,
      error: shown ? snapshot.error : undefined,
      fetching: !pause && (snapshot.fetching || !shown),
      stale: shown && snapshot.fetching,
    }),
    [snapshot, shown, pause],
  );
  return [state, refetch];
}

/** A write: its state, and the function that sends it. The function never throws: an error is in its result. */
export function useTraxMutation<V, R>(
  document: TraxDocument<V, R>,
): [MutationState<R>, (...variables: Record<string, never> extends V ? [variables?: V] : [variables: V]) => Promise<OperationResult<R>>] {
  const client = useConsoleClient();
  const [state, setState] = useState<MutationState<R>>({ fetching: false });
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  const execute = useCallback(
    async (...[variables]: [variables?: V]) => {
      setState({ fetching: true });
      const result = await client.mutation(document, variables);
      if (mounted.current) setState({ fetching: false, data: result.data, error: result.error });
      return result;
    },
    [client, document],
  );
  return [state, execute as (...variables: Record<string, never> extends V ? [variables?: V] : [variables: V]) => Promise<OperationResult<R>>];
}

/**
 * Follows a subscription while mounted and not paused. Without a reducer `data` is the latest frame; with one it is
 * what the reducer made of the frames so far.
 */
export function useTraxSubscription<V, R, T = R>(
  document: TraxDocument<V, R>,
  options?: SubscriptionOptions<V>,
  reducer?: (previous: T | undefined, frame: R) => T | undefined,
): [SubscriptionState<T>] {
  const client = useConsoleClient();
  const variables = (options as { variables?: V } | undefined)?.variables;
  const key = keyOf(document, variables);
  const pause = options?.pause ?? false;
  const [state, setState] = useState<SubscriptionState<T>>({ fetching: !pause });
  const latest = useRef({ document, variables, reducer });
  useEffect(() => {
    latest.current = { document, variables, reducer };
  });

  useEffect(() => {
    if (pause) return;
    const { document: d, variables: v } = latest.current;
    return client.subscribe(
      d,
      v,
      (frame) =>
        setState((previous) => {
          const reduce = latest.current.reducer;
          return { fetching: true, data: reduce ? reduce(previous.data, frame) : (frame as unknown as T) };
        }),
      (error) => setState((previous) => ({ ...previous, fetching: false, error })),
    );
  }, [client, key, pause]);

  return [state];
}
