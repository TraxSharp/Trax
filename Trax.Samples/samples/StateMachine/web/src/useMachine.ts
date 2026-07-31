import { useCallback, useEffect, useState } from "react";
import type { Problem, Result, Snapshot, TraxTransport } from "./traxTransport";

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

  const apply = useCallback((r: Result) => {
    if (r.snapshot) setSnapshot(r.snapshot);
    setProblem(r.problem);
  }, []);

  const run = useCallback(
    async (fn: () => Promise<Result>) => {
      setBusy(true);
      try {
        apply(await fn());
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
    state: snapshot?.state ?? null,
    context: snapshot?.context ?? {},
    save: (s: Snapshot) => run(() => transport.save(machine, id, s)),
    advance: (trigger: string, input?: unknown, requestId?: string) =>
      run(() => transport.advance(machine, id, trigger, input, requestId)),
    send: (requestId?: string) => run(() => transport.send(machine, id, requestId)),
    reload,
  };
}
