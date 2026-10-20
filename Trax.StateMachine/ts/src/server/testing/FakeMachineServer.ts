import type {
  EventResponse,
  GetResponse,
  MachineServer,
  ServerSnapshot,
  StartResponse,
  StreamMessage,
} from '../serverController';

// Test code: an in-memory MachineServer whose every answer the test gives by hand. Each call is recorded in
// order and left pending until the test resolves it (or fails it, as a transport error), so a test controls
// exactly when the client hears back and in what order.

export type EventRequest = Parameters<MachineServer['event']>[0];

/** A call the fake has received and not necessarily answered. */
export class PendingCall<Request, Response> {
  private settledAs: 'resolved' | 'failed' | null = null;

  constructor(
    readonly request: Request,
    private readonly onResolve: (response: Response) => void,
    private readonly onFail: (error: Error) => void,
  ) {}

  get settled(): boolean {
    return this.settledAs !== null;
  }

  /** Answers the call. Answering a call twice is a bug in the test, so it throws. */
  resolve(response: Response): void {
    this.settle('resolved');
    this.onResolve(response);
  }

  /** Fails the call the way an unreachable server does: the promise rejects. */
  fail(error: Error = new Error('transport failed')): void {
    this.settle('failed');
    this.onFail(error);
  }

  private settle(as: 'resolved' | 'failed'): void {
    if (this.settledAs !== null) throw new Error(`This call was already ${this.settledAs}.`);
    this.settledAs = as;
  }
}

/** One registered listener on an instance's change stream. */
export class FakeSubscription {
  private closes = 0;

  constructor(
    readonly id: string,
    private readonly onMessage: (message: StreamMessage) => void,
  ) {}

  get open(): boolean {
    return this.closes === 0;
  }

  /** How many times the client called the close function; a well-behaved client calls it once. */
  get closeCount(): number {
    return this.closes;
  }

  /** Delivers a message on an open stream. Pushing on a closed one is a bug in the test, so it throws. */
  push(message: StreamMessage): void {
    if (!this.open) throw new Error(`The subscription to '${this.id}' is closed; use pushLate for a late message.`);
    this.onMessage(message);
  }

  /** Delivers a message that was already in flight when the client closed the stream. */
  pushLate(message: StreamMessage): void {
    this.onMessage(message);
  }

  close(): void {
    this.closes++;
  }
}

export class FakeMachineServer implements MachineServer {
  /** Every operation in the order it arrived: `start`, `event`, `get`, `subscribe`, `unsubscribe`. */
  readonly log: string[] = [];
  readonly starts: PendingCall<{ input: unknown; requestId: string }, StartResponse>[] = [];
  readonly events: PendingCall<EventRequest, EventResponse>[] = [];
  readonly gets: PendingCall<{ id: string }, GetResponse>[] = [];
  readonly subscriptions: FakeSubscription[] = [];

  start(input: unknown, requestId: string): Promise<StartResponse> {
    this.log.push('start');
    return this.pending(this.starts, { input, requestId });
  }

  event(request: EventRequest): Promise<EventResponse> {
    this.log.push('event');
    return this.pending(this.events, request);
  }

  get(id: string): Promise<GetResponse> {
    this.log.push('get');
    return this.pending(this.gets, { id });
  }

  subscribe(id: string, onMessage: (message: StreamMessage) => void): () => void {
    this.log.push('subscribe');
    const subscription = new FakeSubscription(id, onMessage);
    this.subscriptions.push(subscription);
    return () => {
      this.log.push('unsubscribe');
      subscription.close();
    };
  }

  /** The open subscriptions, optionally of one instance. */
  openSubscriptions(id?: string): FakeSubscription[] {
    return this.subscriptions.filter((s) => s.open && (id === undefined || s.id === id));
  }

  /** The single open subscription; throws unless there is exactly one, so a test never pushes on the wrong one. */
  stream(id?: string): FakeSubscription {
    const open = this.openSubscriptions(id);
    if (open.length !== 1) throw new Error(`Expected one open subscription, found ${open.length}.`);
    return open[0];
  }

  private pending<Request, Response>(
    list: PendingCall<Request, Response>[],
    request: Request,
  ): Promise<Response> {
    return new Promise<Response>((resolve, reject) => {
      list.push(new PendingCall<Request, Response>(request, resolve, reject));
    });
  }
}

/** A server snapshot with defaults: incarnation `row-1`, revision 1, the turnstile's Locked state. */
export function serverSnapshot(overrides: Partial<ServerSnapshot> = {}): ServerSnapshot {
  return { rowId: 'row-1', revision: 1, state: 'Locked', context: {}, ...overrides };
}

/** Lets every pending promise continuation run. Uses microtasks only, so it works under fake timers too. */
export async function flushMicrotasks(rounds = 20): Promise<void> {
  for (let i = 0; i < rounds; i++) await Promise.resolve();
}

/** A requestId generator that yields `rq-1`, `rq-2`, ... so a test can tell ids apart. */
export function sequentialRequestIds(prefix = 'rq'): () => string {
  let n = 0;
  return () => `${prefix}-${++n}`;
}
