// Minimal toast store: replaces window.alert for mutation results. Pub/sub so a single
// ToastHost renders the stack.
export type ToastKind = "success" | "error" | "info" | "warning";
export interface Toast {
  id: number;
  message: string;
  kind: ToastKind;
}

let seq = 0;
let toasts: Toast[] = [];
// How many toasts show at once; the oldest goes when a new one would pass it. Unlimited unless set
// (the demo build keeps it to two).
let limit = Infinity;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((fn) => fn());
}

export function toast(message: string, kind: ToastKind = "info") {
  const t: Toast = { id: ++seq, message, kind };
  toasts = [...toasts, t].slice(-limit);
  emit();
  setTimeout(() => dismiss(t.id), 4500);
}

export function dismiss(id: number) {
  toasts = toasts.filter((t) => t.id !== id);
  emit();
}

export function getToasts(): Toast[] {
  return toasts;
}

/** Caps how many toasts show at once; `Infinity` lifts the cap. */
export function setToastLimit(max: number) {
  limit = Math.max(1, max);
  if (toasts.length > limit) {
    toasts = toasts.slice(-limit);
    emit();
  }
}

/** Clear all toasts (used to isolate tests). */
export function resetToasts() {
  toasts = [];
  emit();
}

export function subscribeToasts(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}
