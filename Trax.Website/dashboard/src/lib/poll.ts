import { useEffect, useRef, useSyncExternalStore } from "react";
import { noteRefresh } from "./refreshStatus";

// Auto-refresh interval (seconds) shared across the list pages, persisted in localStorage. 0 = off.
// The dashboard lives-updates Executions and Overview via subscriptions; the other lists are
// request/response, so this drives their periodic refetch (matching the Blazor dashboard's poller).
const KEY = "trax:poll-seconds";
const DEFAULT = 0;

const listeners = new Set<() => void>();
let current = read();

function read(): number {
  try {
    const raw = globalThis.localStorage?.getItem(KEY);
    const n = raw == null ? DEFAULT : Number(raw);
    return Number.isFinite(n) && n >= 0 ? n : DEFAULT;
  } catch {
    return DEFAULT;
  }
}

export function getPollSeconds(): number {
  return current;
}

export function setPollSeconds(seconds: number): void {
  const n = Number.isFinite(seconds) && seconds >= 0 ? Math.floor(seconds) : 0;
  current = n;
  try {
    globalThis.localStorage?.setItem(KEY, String(n));
  } catch {
    /* ignore */
  }
  listeners.forEach((fn) => fn());
}

function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

/** Current auto-refresh interval in seconds (reactive). */
export function usePollSeconds(): number {
  return useSyncExternalStore(subscribe, getPollSeconds, getPollSeconds);
}

/** Calls `refetch` every `pollSeconds` while a positive interval is set. No-op when off. */
export function usePoll(refetch: () => void): void {
  const seconds = usePollSeconds();
  // Hold the latest refetch in a ref so the interval only resets when the interval changes, not on
  // every render (refetch is a fresh closure each render). Update the ref in an effect, not during
  // render.
  const ref = useRef(refetch);
  useEffect(() => {
    ref.current = refetch;
  });
  useEffect(() => {
    if (seconds <= 0) return;
    // The header counts down to the next refresh from the last one noted here.
    noteRefresh();
    const id = setInterval(() => {
      noteRefresh();
      ref.current();
    }, seconds * 1000);
    return () => clearInterval(id);
  }, [seconds]);
}
