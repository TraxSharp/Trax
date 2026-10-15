import { useEffect, useRef, useState } from "react";
import { useRememberedCheckout, useRememberedTurnstile } from "./trax/index.g";

/** A stored snapshot, as the page shows it. */
export interface Snapshot {
  machine: string;
  version: number;
  state: string;
  context: Record<string, unknown>;
}

export interface Problem {
  code: string;
  message: string;
}

/** What one request came back with: the snapshot the server stored, or why it refused. */
export interface Result {
  snapshot: Snapshot | null;
  problem: Problem | null;
}

/** What the page last asked the server to do, and what came back: the walkthrough explains it. */
export interface LastAction {
  kind: "start" | "advance" | "send";
  /** The trigger an event fired. */
  trigger?: string;
  /** The trigger input. */
  input?: Record<string, unknown>;
  /** The state the stored instance was in before the request. */
  from: string | null;
  result: Result;
  /** A send that returned the receipt the instance already had: the effect did not run again. */
  replayed?: boolean;
  at: number;
}

// The generated hook of each machine. A stage shows one machine for one user and is remounted for another (its React
// key names both), so the hook a stage calls never changes while it is mounted.
const HOOKS = { turnstile: useRememberedTurnstile, checkout: useRememberedCheckout };

/** The trigger whose move runs the machine's effect, when it has one. */
const EFFECT_TRIGGER: Record<keyof typeof HOOKS, string | undefined> = { turnstile: undefined, checkout: "Pay" };

type Outcome =
  | { outcome: "confirmed"; snapshot: unknown }
  | { outcome: "refused" | "failed"; problem: Problem }
  | { outcome: "stopped"; error: unknown };

/**
 * One machine instance for one user, through the generated client: the instance this browser remembers for the user,
 * or a new one the server starts. Every event is sent to the server, which applies it or refuses it; the page keeps
 * the last request and its answer for the walkthrough.
 */
export function useMachineStage(machine: keyof typeof HOOKS, user: string) {
  // The key the generated hook keeps this user's instance under: `user` is the one signIn gave configureTrax.
  const rememberKey = `trax.${machine}.${encodeURIComponent(user)}`;
  const [started] = useState(() => {
    try {
      return localStorage.getItem(rememberKey) === null;
    } catch {
      return true;
    }
  });
  // The generated hook keeps the instance of the user signIn gave configureTrax.
  const m = HOOKS[machine]();
  const [last, setLast] = useState<LastAction | null>(null);
  const announced = useRef(false);

  const snapshot = (m.snapshot as Snapshot | null) ?? null;

  // The server answers NOT_FOUND for an instance that is missing and for one that is not this user's alike, so the hook
  // keeps it. Under this user's own key it can only be missing (the database was reset, say): start a new one.
  const { error, forget } = m;
  useEffect(() => {
    if (error?.code === "GONE" && error.deleted === false) forget();
  }, [error, forget]);

  // A stage that started a new instance says so once the server answers with it.
  useEffect(() => {
    if (!started || announced.current || m.status !== "ready" || !snapshot) return;
    announced.current = true;
    setLast({ kind: "start", from: null, result: { snapshot, problem: null }, at: Date.now() });
  }, [started, m.status, snapshot]);

  const fire = async (trigger: string, input?: Record<string, unknown>) => {
    const before = snapshot;
    // The page's buttons name triggers as strings, for either machine; the server decides what each one means.
    const send = m.send as unknown as (trigger: string, input?: unknown) => Promise<Outcome>;
    const outcome = input === undefined ? await send(trigger) : await send(trigger, input);
    const result: Result =
      outcome.outcome === "confirmed"
        ? { snapshot: outcome.snapshot as Snapshot, problem: null }
        : outcome.outcome === "stopped"
          ? { snapshot: null, problem: { code: "STOPPED", message: "The instance is gone, or the page is out of date." } }
          : { snapshot: null, problem: outcome.problem };
    const receipt = (s: Snapshot | null) => s?.context.receipt as string | null | undefined;
    const sending = trigger === EFFECT_TRIGGER[machine];
    setLast({
      kind: sending ? "send" : "advance",
      trigger,
      input,
      from: before?.state ?? null,
      result,
      replayed: sending && !!receipt(before) && receipt(before) === receipt(result.snapshot),
      at: Date.now(),
    });
    return result;
  };

  return {
    id: m.id,
    snapshot,
    problem: m.problem as Problem | null,
    busy: m.pending || m.status === "loading",
    last,
    state: snapshot?.state ?? null,
    context: snapshot?.context ?? {},
    advance: (trigger: string, input?: Record<string, unknown>) => fire(trigger, input),
    /** Sends the event bound to the machine's effect: the server runs the effect itself, exactly once. */
    send: () => fire(EFFECT_TRIGGER[machine] ?? "Pay"),
  };
}

export type MachineStageHandle = ReturnType<typeof useMachineStage>;
