import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react';
import type { Problem } from '../problem';
import type { InputArgs, MachineSpec, StateOf, TriggerOf, TypedMachine, TypedSnapshot } from '../typed';
import {
  ServerMachineController,
  type MachineServer,
  type SendOutcome,
  type ServerMachineError,
  type ServerMachineView,
} from './serverController';

// The React binding over ServerMachineController: one instance, owned by the server, predicted by the twin.

/** How {@link useServerMachine} reaches the server and which instance it opens. */
export interface UseServerMachineOptions {
  server: MachineServer;
  /** An existing instance to open. Without it, the hook starts one with `startInput` (once, even in StrictMode). */
  id?: string;
  startInput?: unknown;
  /**
   * The requestId of the `start`, when the caller keeps one (across a reload, say, so a start that was in flight is
   * answered with the instance it created). Minted once per (machine, server) when omitted. A different one starts a
   * new instance; it is ignored with `id`.
   */
  startRequestId?: string;
  /**
   * Where the hook keeps the requestId it minted for a `start` until the start answers, in `sessionStorage`, so a
   * reload while the start is unanswered (or after it failed) sends the same id again and the server answers with
   * the instance the first request created rather than a second one. Defaults to `trax-start:<machine id>`; give
   * each hook that starts the same machine on one page its own key, or `null` to keep nothing. Unused with `id` or
   * `startRequestId`. Without `sessionStorage` (a private window that refuses it, or no browser), nothing is kept.
   */
  startStorageKey?: string | null;
  /**
   * Mints a requestId; defaults to `crypto.randomUUID`. A custom one must mint 1 to 200 characters, not blank, with no
   * control character and no unpaired surrogate: the server answers any other id `invalid-request-id`.
   */
  newRequestId?: () => string;
  /** Delays, in milliseconds, between retries of a `server-outdated` answer or a failed read. */
  retryDelays?: readonly number[];
}

/** What {@link useServerMachine} renders from. */
export interface UseServerMachineResult<Spec extends MachineSpec> {
  /** `loading` until the server's first snapshot, `stopped` for good after `gone` or `client-outdated`. */
  status: 'loading' | 'ready' | 'stopped';
  /** The instance id: the one given, or the one the server minted on start. */
  id: string | null;
  /** The prediction while an event is pending, else the server's snapshot; null until the first one. */
  snapshot: TypedSnapshot<Spec> | null;
  state: StateOf<Spec> | null;
  context: TypedSnapshot<Spec>['context'] | null;
  /** The revision of the last snapshot the server confirmed; null until the first one. */
  revision: number | null;
  /** When the state's timer is due, as the server reported it beside the snapshot. */
  dueAt: string | null;
  /** Whether an event is in flight or queued. */
  pending: boolean;
  /** The last refusal or transport failure, shown until the next successful event. */
  problem: Problem | null;
  /** Why the hook stopped: the instance is `gone`, or this page is `client-outdated` and must reload. */
  error: ServerMachineError | null;
  /** Sends one event to the server, after every earlier one has settled. Never throws. */
  send<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): Promise<SendOutcome>;
  /**
   * Resends what last failed (a transport failure, or `server-outdated` past every retry) under its first requestId,
   * so the server replays its result rather than applying it twice: the `start` while there is no instance yet, else
   * the last event that failed, unless a later event has been confirmed since, which supersedes it. `send` again would
   * be a new request. Never throws.
   */
  retry(): Promise<SendOutcome>;
  /** Whether the twin allows the event from what is shown. False while loading and once stopped. */
  can<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): boolean;
  /** The events the twin allows from what is shown. */
  available: TriggerOf<Spec>[];
}

const LOADING: ServerMachineView<MachineSpec> = {
  status: 'loading',
  id: null,
  snapshot: null,
  confirmed: null,
  rowId: null,
  revision: null,
  dueAt: null,
  pending: false,
  problem: null,
  error: null,
};

const NOT_READY: SendOutcome = {
  outcome: 'failed',
  problem: { kind: 'rejected', code: 'not-ready', message: 'The machine has not opened yet.' },
};

const noSubscription = () => () => {};

// sessionStorage, guarded: it can be missing (no browser) or throw (a private window, blocked site data).
function sessionRead(key: string): string | null {
  try {
    return globalThis.sessionStorage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

function sessionWrite(key: string, value: string | null): void {
  try {
    if (value === null) globalThis.sessionStorage?.removeItem(key);
    else globalThis.sessionStorage?.setItem(key, value);
  } catch {
    // Nothing is kept: a reload mints a new requestId, as without the key.
  }
}

// Whether the start's requestId is spent: the instance exists, the hook stopped, or the server refused the start
// for a reason a resend would not change.
function startSettled(view: ServerMachineView<MachineSpec>): boolean {
  if (view.id !== null || view.status === 'stopped') return true;
  const problem = view.problem;
  return problem !== null && problem.kind !== 'transport' && problem.code !== 'server-outdated';
}

/**
 * Renders one instance of a machine the server owns. The server applies every transition, stores it and returns
 * what it stored; the generated twin only predicts, so the page moves at once and the server's answer, or its push,
 * replaces the prediction.
 *
 * - With `id`, the hook opens that instance; without one, it starts a new one with `startInput`, once, under one
 *   requestId even when StrictMode mounts it twice (the server answers a repeated start with the same instance).
 * - It subscribes to the instance's changes before reading it, keeps the newest revision of whatever arrives, and
 *   reads again only after a reconnect, an incarnation change or a `client-divergence`.
 * - `gone` and `client-outdated` stop it for good: `status` is `stopped`, `error` says which, and nothing more is
 *   sent. A page that gets `client-outdated` was built for another version of the machine and must reload.
 * - A transport failure is not retried by itself: `retry` resends the failed start or event under its first
 *   requestId, until a later event is confirmed. A start's minted requestId is kept in `sessionStorage` until it
 *   answers (`startStorageKey`).
 * - `dueAt` is shown, never fired: the server runs a state's timer and pushes the result. Null means no timer, or one
 *   the server cleared.
 *
 * Changing `server`, `machine`, `id` or `startRequestId` closes the old instance's stream and opens the new one; unmounting closes it
 * and drops every late answer. Built over the framework-free {@link ServerMachineController}.
 */
export function useServerMachine<Spec extends MachineSpec>(
  machine: TypedMachine<Spec>,
  options: UseServerMachineOptions,
): UseServerMachineResult<Spec> {
  const { server, id } = options;
  const givenStartRequestId = id === undefined ? options.startRequestId : undefined;
  const [controller, setController] = useState<ServerMachineController<Spec> | null>(null);

  // The options that may change identity on every render are read when a controller is built, never as triggers.
  const latest = useRef(options);
  latest.current = options;

  // One start requestId per (machine, server) the hook starts on, kept across StrictMode's second mount, so the
  // server answers both starts with one instance.
  const startKey = useRef<{ machine: TypedMachine<Spec>; server: MachineServer; requestId: string } | null>(null);

  useEffect(() => {
    const { startInput, newRequestId, retryDelays } = latest.current;
    let startRequestId: string | undefined;
    let storageKey: string | null = null;
    if (givenStartRequestId !== undefined) startRequestId = givenStartRequestId;
    else if (id === undefined) {
      const given = latest.current.startStorageKey;
      storageKey = given === undefined ? `trax-start:${machine.id}` : given;
      const key = startKey.current;
      if (!key || key.machine !== machine || key.server !== server) {
        const kept = storageKey === null ? null : sessionRead(storageKey);
        startKey.current = {
          machine,
          server,
          requestId: kept ?? (newRequestId ?? (() => globalThis.crypto.randomUUID()))(),
        };
      }
      startRequestId = startKey.current!.requestId;
    }

    const next = new ServerMachineController<Spec>({
      machine,
      server,
      id,
      startInput,
      startRequestId,
      newRequestId,
      retryDelays,
    });
    let forget: (() => void) | undefined;
    if (storageKey !== null && startRequestId !== undefined) {
      // Kept until the start is spent; only this hook's own id is removed, never one another hook wrote since.
      const keyName = storageKey;
      const requestId = startRequestId;
      sessionWrite(keyName, requestId);
      forget = next.subscribe(() => {
        if (!startSettled(next.get() as unknown as ServerMachineView<MachineSpec>)) return;
        if (sessionRead(keyName) === requestId) sessionWrite(keyName, null);
        forget?.();
      });
    }
    setController(next);
    void next.start();
    return () => {
      forget?.();
      next.dispose();
    };
  }, [machine, server, id, givenStartRequestId]);

  const view = useSyncExternalStore(
    controller ? (listener) => controller.subscribe(listener) : noSubscription,
    () => (controller ? controller.get() : (LOADING as unknown as ServerMachineView<Spec>)),
    () => (controller ? controller.get() : (LOADING as unknown as ServerMachineView<Spec>)),
  );

  const send = useCallback(
    <T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): Promise<SendOutcome> =>
      controller ? controller.send(trigger, ...input) : Promise.resolve(NOT_READY),
    [controller],
  );
  const retry = useCallback(
    (): Promise<SendOutcome> => (controller ? controller.retry() : Promise.resolve(NOT_READY)),
    [controller],
  );
  const can = useCallback(
    <T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): boolean =>
      controller ? controller.can(trigger, ...input) : false,
    [controller],
  );

  return {
    status: view.status,
    id: view.id,
    snapshot: view.snapshot,
    state: view.snapshot ? view.snapshot.state : null,
    context: view.snapshot ? view.snapshot.context : null,
    revision: view.revision,
    dueAt: view.dueAt,
    pending: view.pending,
    problem: view.problem,
    error: view.error,
    send,
    retry,
    can,
    available: controller ? controller.available() : [],
  };
}
