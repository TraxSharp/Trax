import { useSyncExternalStore } from "react";
import { isDemo } from "./demo";

// Whether to hide the internal/administrative scheduler trains (JobDispatcher, ManifestManager,
// JobRunner, cleanup) from the Executions grid, its live feed, and the Overview metrics. Persisted
// in localStorage and shared across pages. Defaults to true, matching the Blazor dashboard: the
// admin surface streams every train, but operators usually want their own trains, not the plumbing.
// The demo keeps its own key, so a "show" left by an earlier visit to an older demo does not carry over.
const KEY = isDemo ? "trax:demo:hide-admin-trains" : "trax:hide-admin-trains";
const DEFAULT = true;

const listeners = new Set<() => void>();
let current = read();

function read(): boolean {
  try {
    const raw = globalThis.localStorage?.getItem(KEY);
    return raw == null ? DEFAULT : raw === "true";
  } catch {
    return DEFAULT;
  }
}

export function getHideAdminTrains(): boolean {
  return current;
}

export function setHideAdminTrains(hide: boolean): void {
  current = hide;
  try {
    globalThis.localStorage?.setItem(KEY, String(hide));
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

/** Current hide-admin-trains preference (reactive). */
export function useHideAdminTrains(): boolean {
  return useSyncExternalStore(subscribe, getHideAdminTrains, getHideAdminTrains);
}
