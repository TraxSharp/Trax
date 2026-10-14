import { useSyncExternalStore } from "react";

// What the header's refresh indicator shows: when the auto-refresh poll last fired (usePoll notes
// it) and whether the most recent query failed (the activity exchange notes every query result).
// A failed refresh stays marked until a later query succeeds, as the Blazor header's
// "Last refresh failed" does.
export interface RefreshStatus {
  lastRefreshAt: number | null;
  lastError: string | null;
}

let state: RefreshStatus = { lastRefreshAt: null, lastError: null };
const listeners = new Set<() => void>();

function set(next: Partial<RefreshStatus>) {
  const merged = { ...state, ...next };
  if (merged.lastRefreshAt === state.lastRefreshAt && merged.lastError === state.lastError) return;
  state = merged;
  listeners.forEach((fn) => fn());
}

/** The auto-refresh poll fired (or started) now. */
export function noteRefresh(at: number = Date.now()): void {
  set({ lastRefreshAt: at });
}

/** A query finished: an error marks the refresh failed, a success clears the mark. */
export function noteQueryResult(error: string | null): void {
  set({ lastError: error });
}

export function getRefreshStatus(): RefreshStatus {
  return state;
}

/** Reset to the initial state (used to isolate tests). */
export function resetRefreshStatus(): void {
  set({ lastRefreshAt: null, lastError: null });
}

function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

export function useRefreshStatus(): RefreshStatus {
  return useSyncExternalStore(subscribe, getRefreshStatus, getRefreshStatus);
}
