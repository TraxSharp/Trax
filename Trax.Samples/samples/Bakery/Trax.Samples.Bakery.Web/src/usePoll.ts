import { useEffect, useState } from "react";

/** Reads `read` now and every second after, keeping the last answer and the last error. */
export function usePoll<T>(read: () => Promise<T>): { value: T | null; error: string | null } {
  const [value, setValue] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let live = true;
    const tick = () =>
      read().then(
        (v) => live && (setValue(v), setError(null)),
        (e: unknown) => live && setError(e instanceof Error ? e.message : String(e)),
      );
    tick();
    const timer = setInterval(tick, 1000);
    return () => {
      live = false;
      clearInterval(timer);
    };
  }, [read]);
  return { value, error };
}
