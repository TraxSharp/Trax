import { useEffect, useState } from "react";
import { signOutOnRefusal } from "./session";

/**
 * Reads `read` now and a second after each read settles, keeping the last answer and the last error. A refusal for a
 * person the host does not know signs them out instead.
 */
export function usePoll<T>(read: () => Promise<T>): { value: T | null; error: string | null } {
  const [value, setValue] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(
    () =>
      poll(
        read,
        (v) => (setValue(v), setError(null)),
        (e) => {
          if (!signOutOnRefusal(e)) setError(e instanceof Error ? e.message : String(e));
        },
        1000,
      ),
    [read],
  );
  return { value, error };
}

/**
 * Reads `read` now, then `everyMs` after each read settles, so no two reads are in flight and an older answer never
 * lands after a newer one. Answers nothing once stopped.
 */
export function poll<T>(
  read: () => Promise<T>,
  onValue: (value: T) => void,
  onError: (error: unknown) => void,
  everyMs: number,
): () => void {
  let live = true;
  let timer: ReturnType<typeof setTimeout> | undefined;
  const tick = () =>
    read()
      .then(
        (v) => live && onValue(v),
        (e: unknown) => live && onError(e),
      )
      .finally(() => {
        if (live) timer = setTimeout(tick, everyMs);
      });
  tick();
  return () => {
    live = false;
    clearTimeout(timer);
  };
}
