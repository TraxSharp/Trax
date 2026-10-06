import { useCallback, useEffect, useRef, useState } from "react";
import type { Problem, Result, Snapshot, TraxTransport } from "./traxTransport";

/** What the page last asked the server to do, and what came back: the walkthrough explains it. */
export interface LastAction {
  kind: "advance" | "save" | "send";
  /** The trigger an advance fired. */
  trigger?: string;
  /** The trigger input, or for a save the context it wrote. */
  input?: Record<string, unknown>;
  /** The state the stored draft was in before the request. */
  from: string | null;
  result: Result;
  /** A send that returned the receipt the draft already had: the effect did not run again. */
  replayed?: boolean;
  at: number;
}

// Owns one machine instance's snapshot. On mount it resumes the caller's stored draft (or seeds a fresh one
// if there is none), and every action re-renders, including a rejection, so a declined action is never
// silently swallowed. The whole hook is machine-agnostic; the caller supplies the initial snapshot factory.
export function useMachine(
  transport: TraxTransport,
  machine: string,
  id: string,
  initial: () => Snapshot,
) {
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
      } catch (e) {
        setProblem({ code: "transport", message: e instanceof Error ? e.message : String(e) });
      } finally {
        setBusy(false);
      }
    },
    [apply],
  );

  const reload = useCallback(async () => {
    setBusy(true);
    try {
      const loaded = await transport.load(machine, id);
      if (loaded.snapshot) {
        setSnapshot(loaded.snapshot);
        setProblem(null);
      } else {
        // No draft yet (a fresh start): seed one via the soft save path.
        apply(await transport.save(machine, id, initial()));
      }
    } catch (e) {
      setProblem({ code: "transport", message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusy(false);
    }
  }, [transport, machine, id, initial, apply]);

  useEffect(() => {
    void reload();
  }, [reload]);

  return {
    snapshot,
    problem,
    busy,
    last,
    state: snapshot?.state ?? null,
    context: snapshot?.context ?? {},
    save: (s: Snapshot) => run(() => transport.save(machine, id, s), { kind: "save", input: s.context }),
    advance: (trigger: string, input?: Record<string, unknown>, requestId?: string) =>
      run(() => transport.advance(machine, id, trigger, input, requestId), { kind: "advance", trigger, input }),
    send: (requestId?: string) => run(() => transport.send(machine, id, requestId), { kind: "send" }),
    reload,
  };
}

export type MachineHandle = ReturnType<typeof useMachine>;
