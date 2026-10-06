import { useSyncExternalStore } from "react";

// Global "is anything in flight" flag, driven by the urql activity exchange (it counts queries and
// mutations). A single top-of-page progress bar reads it so every network operation shows feedback,
// including long ones like acknowledging thousands of dead letters.
let active = 0;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((fn) => fn());
}

export function setActiveCount(n: number): void {
  const next = Math.max(0, n);
  if (next === active) return;
  active = next;
  emit();
}

export function isActive(): boolean {
  return active > 0;
}

/** Reset to idle (used to isolate tests). */
export function resetActivity(): void {
  setActiveCount(0);
}

function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

/** Whether any query/mutation is currently in flight (reactive). */
export function useActivity(): boolean {
  return useSyncExternalStore(subscribe, isActive, () => false);
}
