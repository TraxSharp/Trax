import { ClientProblemCode, RefusalCode, type ProblemCode } from '../results';
import type { Problem } from '../problem';
import type { InputArgs, MachineSpec, TriggerOf, TypedMachine, TypedSnapshot } from '../typed';

// The server-authoritative controller: the server owns the instance, the twin only predicts. Every event goes
// to the server under its own requestId, the server's answer (or a pushed snapshot) replaces the prediction,
// and snapshots are ordered by `revision` within one incarnation (`rowId`): anything at or below the revision
// held is dropped, the controller's own answers included. See
// Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md.

/** One full server snapshot, with the values that travel beside it rather than inside the canonical wire. */
export interface ServerSnapshot {
  /**
   * The row's incarnation: a delete and re-create gives a new one, which restarts `revision`. Absent when the answer
   * does not carry it (the typed `get` and event answers carry only the revision); the controller then takes it to be
   * the incarnation it holds.
   */
  rowId?: string | null;
  /** Bumped by the database on every write of the row. */
  revision: number;
  state: string;
  context: Record<string, unknown>;
  /**
   * When the state's timer is due (ISO-8601 instant), from the database clock; null when the state has no timer or
   * the server cleared it, which the controller always honours. Absent when the answer does not carry it or cannot
   * know it (a replay of a request the instance has moved past): the controller then keeps the due time it holds
   * while the state stays, and otherwise reads the instance once for it if the state is timed.
   */
  dueAt?: string | null;
}

/** A refusal the server answers with. `code` is the contract (`GUARD_FAILED`, `CLIENT_DIVERGENCE`, ...). */
export interface ServerProblem {
  kind: 'problem';
  code: ProblemCode;
  message: string;
}

/** The answer to `start`: the new instance's id and its first snapshot, or a refusal. */
export type StartResponse = { kind: 'started'; id: string; snapshot: ServerSnapshot } | ServerProblem;
/** The answer to one event: the snapshot the server stored, or a refusal. */
export type EventResponse = { kind: 'ok'; snapshot: ServerSnapshot } | ServerProblem;
/** The answer to `get`: the instance's current snapshot, or a refusal (`NOT_FOUND` when there is none to read). */
export type GetResponse = { kind: 'ok'; snapshot: ServerSnapshot } | ServerProblem;

/** What the change stream of one instance delivers. */
export type StreamMessage =
  | { type: 'snapshot'; snapshot: ServerSnapshot }
  /**
   * The instance was deleted. Needs no re-read. With `notFound`, the server answered NOT_FOUND instead, which it
   * answers alike for an instance that is missing and one that is not the caller's.
   */
  | { type: 'gone'; notFound?: boolean }
  /** The transport reconnected after a drop: anything may have been missed. */
  | { type: 'reconnected' }
  /** The stream ended because the server no longer serves this client's version of the machine. */
  | { type: 'client-outdated' }
  /**
   * The transport ended the stream for good, for a reason of its own (a generated client's `CONNECTION_CHANGED`): it is
   * not subscribed again, and the controller stops with this code and message.
   */
  | { type: 'ended'; code: string; message: string };

/**
 * The seam to the server: the machine's own start / event / get operations and its change stream. A
 * rejected promise is a transport error, unless `endedBy` says it ends the controller; a refusal is a resolved
 * {@link ServerProblem}. `createMachineServer` builds one over a host's typed `machines` root and its push
 * subscription.
 */
export interface MachineServer {
  start(input: unknown, requestId: string): Promise<StartResponse>;
  event(request: {
    id: string;
    trigger: string;
    input?: unknown;
    requestId: string;
    /** The canonical wire of the snapshot the twin predicted, when it predicted one. */
    clientResult?: string;
  }): Promise<EventResponse>;
  get(id: string): Promise<GetResponse>;
  /** Registers a listener for the instance's changes; the returned function closes it. */
  subscribe(id: string, onMessage: (message: StreamMessage) => void): () => void;
  /**
   * The code and message a rejected request ends the controller with for good, as an `ended` stream message does
   * (a generated client's `CONNECTION_CHANGED`), or null for a transport failure that `retry` may resend. Absent:
   * every rejection is a transport failure.
   */
  endedBy?(error: unknown): { code: string; message: string } | null;
}

/**
 * Why a controller stopped for good: the instance is gone, this client is out of date and must reload, or the
 * transport ended the stream with a code of its own (an `ended` message, such as `CONNECTION_CHANGED`).
 */
export interface ServerMachineError {
  code: typeof ClientProblemCode.Gone | typeof RefusalCode.ClientOutdated | (string & {});
  message: string;
  /**
   * With `GONE`: true when the stream said the instance was deleted or expired, false when the server answered
   * `NOT_FOUND`, which does not tell a missing instance from one that is not the caller's.
   */
  deleted?: boolean;
}

/** What a controller holds, as one immutable value: a new object on every change, the same one otherwise. */
export interface ServerMachineView<Spec extends MachineSpec> {
  /** `loading` until the first server snapshot, `stopped` after `gone`, `CLIENT_OUTDATED` or `ended`. */
  status: 'loading' | 'ready' | 'stopped';
  /** The instance id, once started or given. */
  id: string | null;
  /** What to show: the newest prediction while one is pending, else the server's snapshot. */
  snapshot: TypedSnapshot<Spec> | null;
  /** The last snapshot the server confirmed. */
  confirmed: TypedSnapshot<Spec> | null;
  rowId: string | null;
  revision: number | null;
  dueAt: string | null;
  /** Whether an event is in flight or queued. */
  pending: boolean;
  /** The last refusal or transport failure, shown until the next successful event. */
  problem: Problem | null;
  error: ServerMachineError | null;
}

/** How a {@link ServerMachineController} is built. */
export interface ServerMachineOptions<Spec extends MachineSpec> {
  machine: TypedMachine<Spec>;
  server: MachineServer;
  /** An existing instance to open. Without it, `start` creates one with `startInput`. */
  id?: string;
  startInput?: unknown;
  /**
   * The requestId of the `start`, minted by `newRequestId` when omitted. A controller that replaces a disposed one
   * passes the same id, so the server answers the retried start with the instance it already created.
   */
  startRequestId?: string;
  /**
   * Mints a requestId; defaults to `crypto.randomUUID`. A custom one must mint 1 to 200 characters, not blank, with no
   * control character and no unpaired surrogate: the server answers any other id `INVALID_REQUEST_ID`.
   */
  newRequestId?: () => string;
  /**
   * Delays, in milliseconds, between retries of a `SERVER_OUTDATED` answer (another node of a rolling deploy answered)
   * and of a read the transport failed. One retry per delay; after the last, the event fails and `retry` resends it.
   */
  retryDelays?: readonly number[];
}

/** What one `send` came to. */
export type SendOutcome =
  | { outcome: 'confirmed'; snapshot: ServerSnapshot }
  | { outcome: 'refused'; problem: Problem }
  | { outcome: 'failed'; problem: Problem }
  | { outcome: 'stopped'; error: ServerMachineError };

/** The problem code of a send or read the transport could not deliver. */
export const TRANSPORT_FAILED = ClientProblemCode.TransportError;

/** The default delays between retries of a `SERVER_OUTDATED` answer or a failed read. */
export const DEFAULT_RETRY_DELAYS: readonly number[] = [250, 500, 1000, 2000, 4000];

const CLIENT_OUTDATED = RefusalCode.ClientOutdated;
const SERVER_OUTDATED = RefusalCode.ServerOutdated;
const CLIENT_DIVERGENCE = RefusalCode.ClientDivergence;
const NOT_FOUND = RefusalCode.NotFound;

type EventRequest = Parameters<MachineServer['event']>[0];

interface QueuedEvent {
  trigger: string;
  input: unknown[];
  /** Fixed once the event is first sent, so a retry carries exactly the same request. */
  request: EventRequest | null;
  settle: (outcome: SendOutcome) => void;
}

const defaultRequestId = (): string => globalThis.crypto.randomUUID();

const refusal = (code: ProblemCode, message: string): Problem => ({ kind: 'rejected', code, message });

const transportFailure = (error: unknown): Problem => ({
  kind: 'transport',
  code: TRANSPORT_FAILED,
  message: error instanceof Error ? error.message : 'The server could not be reached.',
});

/**
 * Drives one instance of a machine the server owns. The server applies every transition; the twin only predicts.
 *
 * - **Events** are sent one at a time per instance, each under its own requestId, in the order `send` was called.
 *   While one is pending, the view shows the twin's prediction (sent as `clientResult`); the server's answer replaces
 *   it, and a refusal or a transport failure rolls it back to the last snapshot the server confirmed. An event the
 *   twin refuses is still sent, without a prediction: the server decides. An effect-bound event (one whose edge runs
 *   an effect) is sent with no input and no prediction, and nothing queued after it is predicted until it answers.
 * - **Ordering.** Every snapshot, pushed or answered, carries its revision. Anything at or below the revision held is
 *   dropped, so a skipped revision is harmless. The controller reads the instance again only after a reconnect, an
 *   incarnation change or a `CLIENT_DIVERGENCE`, once each.
 * - **Retries.** `SERVER_OUTDATED` is retried under the same requestId after each of `retryDelays`, never reloaded:
 *   an event, a read and the `start` that creates the instance alike.
 *   A transport failure ends the event (or the start) as `failed`; `retry` resends it under the same requestId, so
 *   the server replays it rather than applying it twice. A later event the server confirms supersedes it, and
 *   `retry` then has nothing to resend.
 * - **Stopping.** `gone` and `CLIENT_OUTDATED`, from the stream or an answer, stop the controller for good: the
 *   stream is closed and nothing more is sent. So do an `ended` stream message and a request rejected with an error
 *   the server's `endedBy` names; `retry` then sends nothing.
 * - **Timers** are the server's: `dueAt` is shown, never fired. Null means no timer, or one the server cleared; an
 *   answer that cannot know it leaves it out.
 *
 * Framework-free; `useServerMachine` binds it to React through `subscribe` and `get`.
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#talking-to-a-server
 */
export class ServerMachineController<Spec extends MachineSpec> {
  private readonly machine: TypedMachine<Spec>;
  private readonly server: MachineServer;
  private readonly startInput: unknown;
  private startRequestId: string | undefined;
  private readonly newRequestId: () => string;
  private readonly retryDelays: readonly number[];
  private readonly listeners = new Set<() => void>();

  private id: string | null;
  private status: ServerMachineView<Spec>['status'] = 'loading';
  private confirmed: TypedSnapshot<Spec> | null = null;
  private rowId: string | null = null;
  private revision: number | null = null;
  private dueAt: string | null = null;
  private problem: Problem | null = null;
  private error: ServerMachineError | null = null;

  private readonly queue: QueuedEvent[] = [];
  private sending = false;
  private lastFailed: EventRequest | null = null;
  // Whether the `start` that creates the instance failed in a way `retry` resends (a transport failure, or
  // `SERVER_OUTDATED` past every retry), and who waits for the start a retry resent.
  private startFailed = false;
  private readonly startWaiters: ((outcome: SendOutcome) => void)[] = [];

  private disposed = false;
  private started: Promise<void> | null = null;
  private closeStream: (() => void) | null = null;
  private readonly timers = new Set<ReturnType<typeof setTimeout>>();

  // A read of the instance in flight, the incarnation it was asked for (null: the one held), whether another is
  // owed once it answers, and the newest push of that new incarnation seen meanwhile.
  private reloading = false;
  private reloadRowId: string | null = null;
  private reloadAgain = false;
  private buffered: ServerSnapshot | null = null;

  private view: ServerMachineView<Spec>;

  constructor(options: ServerMachineOptions<Spec>) {
    this.machine = options.machine;
    this.server = options.server;
    this.id = options.id ?? null;
    this.startInput = options.startInput;
    this.startRequestId = options.startRequestId;
    this.newRequestId = options.newRequestId ?? defaultRequestId;
    this.retryDelays = options.retryDelays ?? DEFAULT_RETRY_DELAYS;
    this.view = this.buildView();
  }

  /**
   * Starts the instance (or opens the given one): subscribes first, then reads the current snapshot, so no write
   * can fall between the two. Without an id it calls `start` once, under `startRequestId`, and subscribes to the
   * instance the server minted. Calling it again returns the same promise; a start that failed is resent by `retry`.
   */
  start(): Promise<void> {
    this.started ??= this.open();
    return this.started;
  }

  /** Sends one event under a fresh requestId, after every earlier event of this instance has settled. */
  send<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): Promise<SendOutcome> {
    if (this.error) return Promise.resolve({ outcome: 'stopped', error: this.error });
    if (this.disposed) return Promise.resolve(this.disposedOutcome());
    return this.enqueue({ trigger, input: [...input], request: null });
  }

  /**
   * Resends what last failed (a transport failure, or `SERVER_OUTDATED` past every retry) under the same requestId,
   * so the server replays its recorded result if the first attempt did arrive: the `start` that creates the instance
   * while it has none, else the last event that failed, unless a later event has been confirmed since, which
   * supersedes it (resending it then would apply it out of order). A retried start settles `confirmed` with the
   * instance's first snapshot.
   */
  retry(): Promise<SendOutcome> {
    if (this.error) return Promise.resolve({ outcome: 'stopped', error: this.error });
    if (this.disposed) return Promise.resolve(this.disposedOutcome());
    if (this.startFailed && this.id === null && this.startRequestId !== undefined) {
      this.startFailed = false;
      const requestId = this.startRequestId;
      return new Promise<SendOutcome>((resolve) => {
        this.startWaiters.push(resolve);
        void this.create(requestId, 0);
      });
    }
    const request = this.lastFailed;
    if (!request)
      return Promise.resolve({
        outcome: 'failed',
        problem: refusal(ClientProblemCode.NothingToRetry, 'No event has failed since the last one was retried.'),
      });
    this.lastFailed = null;
    return this.enqueue({ trigger: request.trigger, input: request.input === undefined ? [] : [request.input], request });
  }

  /** The current view. The same object until something changes, as `useSyncExternalStore` requires. */
  get(): ServerMachineView<Spec> {
    return this.view;
  }

  /** Calls `listener` after every change of the view; the returned function removes it. */
  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  /** Whether the twin allows `trigger` from what is shown. Always false before the first snapshot and once stopped. */
  can<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): boolean {
    const shown = this.view.snapshot;
    if (!shown || this.status === 'stopped') return false;
    // An effect-bound trigger's guard reads an input only the server's effect runner has, so its rules on that input
    // count as passing and its rules on the context still decide.
    if (this.machine.isEffectBound(trigger)) return this.couldFireWithoutInput(shown, trigger);
    return this.machine.can(shown, trigger, ...input);
  }

  private couldFireWithoutInput(shown: TypedSnapshot<Spec>, trigger: TriggerOf<Spec>): boolean {
    if (!this.machine.available(shown).includes(trigger)) return false;
    try {
      return this.machine.core.definition.transitions.some(
        (t) =>
          t.from === shown.state &&
          t.trigger === trigger &&
          (t.guard == null || t.guard(shown.context, undefined, { inputUnknown: true })),
      );
    } catch {
      return false;
    }
  }

  /** The triggers the twin allows from what is shown; none before the first snapshot or once stopped. */
  available(): TriggerOf<Spec>[] {
    const shown = this.view.snapshot;
    if (!shown || this.status === 'stopped') return [];
    return this.machine.available(shown);
  }

  /** Closes the stream and drops every late answer. Pending sends settle as `failed` with the code `disposed`. */
  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.close();
    for (const timer of this.timers) clearTimeout(timer);
    this.timers.clear();
    for (const queued of this.queue.splice(0)) queued.settle(this.disposedOutcome());
    this.settleStart(this.disposedOutcome());
    this.listeners.clear();
  }

  // ── Opening ────────────────────────────────────────────────────────────────────────────────────

  private async open(): Promise<void> {
    if (this.disposed || this.error) return;
    if (this.id !== null) {
      this.listen(this.id);
      await this.read(null);
      return;
    }
    this.startRequestId ??= this.newRequestId();
    await this.create(this.startRequestId, 0);
  }

  // Calls `start` under one requestId for every attempt, `retry` included, so a retry the server already answered
  // replays the instance it created rather than creating another.
  private async create(requestId: string, attempt: number): Promise<void> {
    if (this.disposed || this.error) return;
    let answer: StartResponse;
    try {
      answer = await this.server.start(this.startInput, requestId);
    } catch (error) {
      if (this.disposed) return;
      if (this.endedBy(error)) {
        this.settleStart({ outcome: 'stopped', error: this.error! });
        return;
      }
      this.problem = transportFailure(error);
      this.startFailed = true;
      this.emit();
      this.settleStart({ outcome: 'failed', problem: this.problem });
      return;
    }
    if (this.disposed) return;
    if (answer.kind === 'problem') {
      if (answer.code === SERVER_OUTDATED && attempt < this.retryDelays.length) {
        // Another node of a rolling deploy answered: retry, and another node may run this version.
        this.later(attempt, () => void this.create(requestId, attempt + 1), () => {});
        return;
      }
      if (answer.code === CLIENT_OUTDATED) {
        this.stop({ code: RefusalCode.ClientOutdated, message: answer.message });
        this.settleStart({ outcome: 'stopped', error: this.error! });
        return;
      }
      this.problem = refusal(answer.code, answer.message);
      const retryable = answer.code === SERVER_OUTDATED;
      this.startFailed = retryable;
      this.emit();
      this.settleStart({ outcome: retryable ? 'failed' : 'refused', problem: this.problem });
      return;
    }
    this.id = answer.id;
    this.problem = null;
    this.listen(answer.id);
    // The stream's first event is the instance as it is then, so a write between the answer and the subscription
    // reaches it there; the answer itself is the snapshot to show until then.
    this.accept(answer.snapshot, 'answer');
    this.emit();
    this.settleStart({ outcome: 'confirmed', snapshot: answer.snapshot });
    this.pump();
  }

  private settleStart(outcome: SendOutcome): void {
    for (const waiter of this.startWaiters.splice(0)) waiter(outcome);
  }

  private listen(id: string): void {
    let open = true;
    const close = this.server.subscribe(id, (message) => {
      if (open && !this.disposed && !this.error) this.onStream(message);
    });
    this.closeStream = () => {
      if (!open) return;
      open = false;
      close();
    };
  }

  private close(): void {
    const close = this.closeStream;
    this.closeStream = null;
    close?.();
  }

  private onStream(message: StreamMessage): void {
    switch (message.type) {
      case 'snapshot':
        if (this.accept(message.snapshot, 'push')) this.emit();
        this.pump();
        return;
      case 'gone':
        this.stop(
          message.notFound
            ? { code: ClientProblemCode.Gone, message: 'The server holds no such instance for this caller.', deleted: false }
            : { code: ClientProblemCode.Gone, message: 'The instance was deleted or expired.', deleted: true },
        );
        return;
      case 'client-outdated':
        this.stop({ code: RefusalCode.ClientOutdated, message: 'The server no longer serves this version of the machine. Reload.' });
        return;
      case 'ended':
        this.stop({ code: message.code, message: message.message });
        return;
      case 'reconnected':
        this.reload(null);
        return;
      default:
        // An unknown message is not a reason to trust anything: read the instance again.
        this.reload(null);
    }
  }

  // ── Reading ────────────────────────────────────────────────────────────────────────────────────

  private reload(rowId: string | null): void {
    if (this.reloading) {
      this.reloadAgain = true;
      return;
    }
    this.reloadRowId = rowId;
    void this.read(rowId);
  }

  private async read(rowId: string | null, attempt = 0): Promise<void> {
    if (this.disposed || this.error || this.id === null) return;
    this.reloading = true;
    this.reloadRowId = rowId;
    const id = this.id;
    let answer: GetResponse;
    try {
      answer = await this.server.get(id);
    } catch (error) {
      if (this.disposed || this.error) return;
      if (this.endedBy(error)) {
        this.endRead();
        return;
      }
      this.problem = transportFailure(error);
      this.emit();
      this.later(attempt, () => void this.read(rowId, attempt + 1), () => this.endRead());
      return;
    }
    if (this.disposed || this.error) return;

    if (answer.kind === 'problem') {
      if (answer.code === SERVER_OUTDATED && attempt < this.retryDelays.length) {
        this.later(attempt, () => void this.read(rowId, attempt + 1), () => this.endRead());
        return;
      }
      this.endRead();
      if (answer.code === CLIENT_OUTDATED) this.stop({ code: RefusalCode.ClientOutdated, message: answer.message });
      else if (answer.code === NOT_FOUND) this.stop({ code: ClientProblemCode.Gone, message: answer.message, deleted: false });
      else {
        this.problem = refusal(answer.code, answer.message);
        this.emit();
      }
      return;
    }

    const snapshot = { ...answer.snapshot, rowId: answer.snapshot.rowId ?? rowId ?? this.rowId };
    let changed = this.accept(snapshot, 'load');
    const buffered = this.buffered;
    this.buffered = null;
    if (buffered && !this.error && buffered.rowId === this.rowId) changed = this.accept(buffered, 'push') || changed;
    this.endRead();
    if (changed && !this.error) this.emit();
    this.pump();
  }

  private endRead(): void {
    this.reloading = false;
    this.reloadRowId = null;
    if (this.reloadAgain && !this.disposed && !this.error) {
      this.reloadAgain = false;
      this.reload(null);
    }
  }

  // ── Ordering ───────────────────────────────────────────────────────────────────────────────────

  /**
   * Takes a server snapshot if it is newer than the one held; returns whether the view changed. A push of another
   * incarnation than the one held starts one read instead, and is kept until that read answers.
   */
  private accept(incoming: ServerSnapshot, source: 'push' | 'answer' | 'load'): boolean {
    const rowId = incoming.rowId ?? null;
    const otherIncarnation = rowId !== null && this.rowId !== null && rowId !== this.rowId;

    if (otherIncarnation && source !== 'load') {
      // The row was deleted and created again: its revisions restart, so they say nothing against the ones held.
      if (!this.reloading) this.reload(rowId);
      else if (this.reloadRowId === null) this.reloadAgain = true;
      if (this.buffered === null || this.buffered.rowId !== rowId || incoming.revision > this.buffered.revision)
        this.buffered = { ...incoming, rowId };
      return false;
    }
    if (!otherIncarnation && this.revision !== null && incoming.revision <= this.revision) {
      // A read of the revision held, asked for because an answer did not know the due time of its timed state,
      // brings the due time and nothing else.
      if (
        source === 'load' &&
        incoming.revision === this.revision &&
        this.dueAt === null &&
        typeof incoming.dueAt === 'string' &&
        this.confirmed?.state === incoming.state
      ) {
        this.dueAt = incoming.dueAt;
        return true;
      }
      return false;
    }

    const parsed = this.machine.rehydrate(
      JSON.stringify({
        machine: this.machine.id,
        version: this.machine.version,
        state: incoming.state,
        context: incoming.context,
      }),
      // The server applied every transition and checked the result, custom rules included; the twin holds no
      // handler for those, so it trusts them here and checks the shape and the built-in rules.
      { fromServer: true },
    );
    if (parsed.result !== 'ok') {
      // The server serves a snapshot this twin cannot read: the page was built for another version of the machine.
      this.stop({
        code: RefusalCode.ClientOutdated,
        message: `The server's snapshot is not one this client's machine can read (${parsed.code}): ${parsed.message}`,
      });
      return false;
    }

    // The due time is the server's, beside the snapshot: a value replaces the one held, and null clears it, in a
    // timed state too (the server stopped trying to fire it). An answer without the field (one that does not carry it,
    // or a replay the row has moved past, which cannot know it) keeps the one held only while the instance stays in
    // the state it was given for, as the server keeps a due time across writes that stay in a timed state; in any
    // other state it is not that state's, and a timed state is read once for it, so it is never shown without its
    // deadline for longer than one read.
    const sameState =
      !otherIncarnation && this.confirmed !== null && this.confirmed.state === parsed.snapshot.state;
    const timed = this.machine.core.definition.timers?.[parsed.snapshot.state] !== undefined;
    if (incoming.dueAt !== undefined) this.dueAt = incoming.dueAt;
    else if (sameState && this.dueAt !== null) {
      // Kept: the same state, and the answer does not know better.
    } else {
      this.dueAt = null;
      if (timed && source !== 'load') this.reload(null);
    }
    this.confirmed = parsed.snapshot;
    this.rowId = rowId ?? this.rowId;
    this.revision = incoming.revision;
    if (this.status === 'loading') this.status = 'ready';
    return true;
  }

  // ── Sending ────────────────────────────────────────────────────────────────────────────────────

  private enqueue(entry: Omit<QueuedEvent, 'settle'>): Promise<SendOutcome> {
    return new Promise<SendOutcome>((resolve) => {
      this.queue.push({ ...entry, settle: resolve });
      this.emit();
      this.pump();
    });
  }

  private pump(): void {
    if (this.sending || this.disposed || this.error || this.id === null || this.confirmed === null) return;
    const next = this.queue[0];
    if (!next) return;
    this.sending = true;

    if (next.request === null) {
      // An effect-bound event is sent without a prediction: what it leads to depends on the effect's result.
      const predicted = this.machine.isEffectBound(next.trigger as TriggerOf<Spec>)
        ? null
        : this.machine.advance(
            this.confirmed,
            next.trigger as TriggerOf<Spec>,
            ...(next.input as InputArgs<Spec, TriggerOf<Spec>>),
          );
      const request: EventRequest = { id: this.id, trigger: next.trigger, requestId: this.newRequestId() };
      if (next.input.length > 0) request.input = next.input[0];
      if (predicted?.outcome === 'transitioned') request.clientResult = this.machine.serialize(predicted.snapshot);
      next.request = request;
    }
    void this.deliver(next, next.request, 0);
  }

  private async deliver(entry: QueuedEvent, request: EventRequest, attempt: number): Promise<void> {
    let answer: EventResponse;
    try {
      answer = await this.server.event(request);
    } catch (error) {
      if (this.disposed || this.error) return;
      // Stopping settles this event, and everything queued after it, as stopped.
      if (this.endedBy(error)) return;
      this.lastFailed = request;
      this.problem = transportFailure(error);
      this.finish(entry, { outcome: 'failed', problem: this.problem });
      return;
    }
    if (this.disposed || this.error) return;

    if (answer.kind === 'ok') {
      this.accept(answer.snapshot, 'answer');
      // A confirmed event supersedes one that failed before it: resending that one now would apply it out of order.
      this.lastFailed = null;
      this.problem = null;
      this.finish(entry, { outcome: 'confirmed', snapshot: answer.snapshot });
      return;
    }

    switch (answer.code) {
      case CLIENT_OUTDATED:
        this.stop({ code: RefusalCode.ClientOutdated, message: answer.message });
        return;
      case SERVER_OUTDATED:
        if (attempt < this.retryDelays.length) {
          this.later(
            attempt,
            () => void this.deliver(entry, request, attempt + 1),
            () => undefined,
          );
          return;
        }
        this.lastFailed = request;
        this.problem = refusal(answer.code, answer.message);
        this.finish(entry, { outcome: 'failed', problem: this.problem });
        return;
      case CLIENT_DIVERGENCE:
        // The twin and the server disagree about this step: what the twin holds cannot be trusted, so read it again.
        this.reload(null);
        break;
    }
    this.problem = refusal(answer.code, answer.message);
    this.finish(entry, { outcome: 'refused', problem: this.problem });
  }

  private finish(entry: QueuedEvent, outcome: SendOutcome): void {
    const index = this.queue.indexOf(entry);
    if (index >= 0) this.queue.splice(index, 1);
    this.sending = false;
    this.emit();
    entry.settle(outcome);
    this.pump();
  }

  // ── Stopping and timing ────────────────────────────────────────────────────────────────────────

  private stop(error: ServerMachineError): void {
    if (this.error || this.disposed) return;
    this.error = error;
    this.status = 'stopped';
    this.close();
    for (const timer of this.timers) clearTimeout(timer);
    this.timers.clear();
    this.sending = false;
    const stranded = this.queue.splice(0);
    this.emit();
    for (const queued of stranded) queued.settle({ outcome: 'stopped', error });
  }

  // Stops for good when the server says a rejection ends the controller; returns whether it did.
  private endedBy(error: unknown): boolean {
    let end: { code: string; message: string } | null | undefined;
    try {
      end = this.server.endedBy?.(error);
    } catch {
      end = null;
    }
    if (!end) return false;
    this.stop({ code: end.code, message: end.message });
    return true;
  }

  // Runs `next` after the attempt's delay, or `exhausted` at once when there is none left.
  private later(attempt: number, next: () => void, exhausted: () => void): void {
    if (attempt >= this.retryDelays.length) {
      exhausted();
      return;
    }
    const timer = setTimeout(() => {
      this.timers.delete(timer);
      if (!this.disposed && !this.error) next();
    }, this.retryDelays[attempt]);
    this.timers.add(timer);
  }

  private disposedOutcome(): SendOutcome {
    return { outcome: 'failed', problem: refusal(ClientProblemCode.Disposed, 'The controller was disposed before the server answered.') };
  }

  private emit(): void {
    if (this.disposed) return;
    this.view = this.buildView();
    for (const listener of [...this.listeners]) listener();
  }

  private buildView(): ServerMachineView<Spec> {
    return {
      status: this.status,
      id: this.id,
      snapshot: this.predicted(),
      confirmed: this.confirmed,
      rowId: this.rowId,
      revision: this.revision,
      dueAt: this.dueAt,
      pending: this.queue.length > 0,
      problem: this.problem,
      error: this.error,
    };
  }

  // The confirmed snapshot with every pending event the twin allows applied in order, up to the first effect-bound
  // one: what the page will show if the server agrees. An event the twin refuses leaves it as it is; the server still
  // decides.
  private predicted(): TypedSnapshot<Spec> | null {
    const confirmed = this.confirmed;
    if (!confirmed || this.status === 'stopped') return confirmed;
    let shown: TypedSnapshot<Spec> = confirmed;
    for (const queued of this.queue) {
      // Nothing is predicted through an effect-bound event, nor past it: what follows depends on the effect.
      if (this.machine.isEffectBound(queued.trigger as TriggerOf<Spec>)) break;
      const next = this.machine.advance(
        shown,
        queued.trigger as TriggerOf<Spec>,
        ...(queued.input as InputArgs<Spec, TriggerOf<Spec>>),
      );
      if (next.outcome === 'transitioned') shown = next.snapshot;
    }
    return shown;
  }
}
