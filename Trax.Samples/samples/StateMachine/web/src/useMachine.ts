import { useCallback, useEffect, useRef, useState } from "react";
import type { Problem, Result, Snapshot, TraxTransport } from "./traxTransport";

/** What the page last asked the server to do, and what came back: the walkthrough explains it. */
export interface LastAction {
  kind: "start" | "advance" | "send";
  /** The trigger an advance fired. */
  trigger?: string;
  /** The trigger input. */
  input?: Record<string, unknown>;
  /** The state the stored draft was in before the request. */
  from: string | null;
  result: Result;
  /** A send that returned the receipt the draft already had: the effect did not run again. */
  replayed?: boolean;
  at: number;
}

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null) {
  try {
    if (value == null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    // A private window keeps nothing between visits; this one still works.
  }
}

// Owns one machine instance's snapshot. On mount it resumes the caller's stored instance, or starts one: the server
// mints the id from the machine's declared start, and this browser keeps it per user and machine. Every action
// re-renders, including a rejection, so a declined action is never silently swallowed. The whole hook is
// machine-agnostic.
export function useMachine(transport: TraxTransport, machine: string, user: string) {
  const idKey = `trax-statemachine.${user}.${machine}`;
  // The request id of a start that has not answered: a reload retries it and gets the same instance back.
  const startKey = `${idKey}.start`;
  const [id, setId] = useState<string | null>(() => read(idKey));
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [busy, setBusy] = useState(false);
  const [last, setLast] = useState<LastAction | null>(null);
  const current = useRef<Snapshot | null>(null);
  current.current = snapshot;

  const apply = useCallback((r: Result) => {
    if (r.snapshot) setSnapshot(r.snapshot);
    setProblem(r.problem);
  }, []);

  const run = useCallback(
    async (fn: () => Promise<Result>, action: Omit<LastAction, "from" | "result" | "at">) => {
      const before = current.current;
      setBusy(true);
      try {
        const result = await fn();
        apply(result);
        const receipt = (s: Snapshot | null | undefined) => s?.context.receipt as string | null | undefined;
        setLast({
          ...action,
          from: before?.state ?? null,
          result,
          replayed: action.kind === "send" && !!receipt(before) && receipt(before) === receipt(result.snapshot),
          at: Date.now(),
        });
        return result;
      } catch (e) {
        setProblem({ code: "transport", message: e instanceof Error ? e.message : String(e) });
        return null;
      } finally {
        setBusy(false);
      }
    },
    [apply],
  );

  const start = useCallback(async () => {
    let requestId = read(startKey);
    if (!requestId) {
      requestId = crypto.randomUUID();
      write(startKey, requestId);
    }
    const started = await run(() => transport.start(machine, requestId), { kind: "start" });
    if (started?.id) {
      write(startKey, null);
      write(idKey, started.id);
      setId(started.id);
    }
  }, [transport, machine, idKey, startKey, run]);

  const reload = useCallback(async () => {
    if (!id) return start();
    setBusy(true);
    try {
      const loaded = await transport.load(machine, id);
      if (loaded.snapshot) {
        setSnapshot(loaded.snapshot);
        setProblem(null);
        return;
      }
      if (loaded.problem?.code !== "not-found") return setProblem(loaded.problem);
    } catch (e) {
      return setProblem({ code: "transport", message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusy(false);
    }
    // The server no longer holds this instance (it expired, or was discarded): start a new one.
    write(idKey, null);
    setId(null);
  }, [transport, machine, id, idKey, start]);

  useEffect(() => {
    void reload();
  }, [reload]);

  return {
    id,
    snapshot,
    problem,
    busy,
    last,
    state: snapshot?.state ?? null,
    context: snapshot?.context ?? {},
    advance: (trigger: string, input?: Record<string, unknown>) =>
      id ? run(() => transport.advance(machine, id, trigger, input, crypto.randomUUID()), { kind: "advance", trigger, input }) : undefined,
    send: (requestId?: string) => (id ? run(() => transport.send(machine, id, requestId), { kind: "send" }) : undefined),
    reload,
  };
}

export type MachineHandle = ReturnType<typeof useMachine>;
