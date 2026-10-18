// What the console asks before it acts (api/0043, and no browser confirm box): one request at a
// time, answered by the ConfirmHost dialog. Pub/sub, like lib/toast.ts, so a page awaits an answer
// without owning the dialog.

export interface ConfirmRequest {
  /** The action, as its button names it: "Cancel run", "Requeue dead letters". */
  title: string;
  /** What will happen, in a sentence or two. */
  message: string;
  /** The confirming button's label; the title when omitted. */
  confirmLabel?: string;
  /** A destructive or wide action: the confirming button is drawn in warning colours. */
  danger?: boolean;
  /** The operator types this before the confirming button enables (for "all" and filter-wide actions). */
  typeToConfirm?: string;
}

interface Pending extends ConfirmRequest {
  resolve: (ok: boolean) => void;
}

let pending: Pending | null = null;
// Tests and stories that drive a page rather than its dialog answer every request this way.
let auto: boolean | null = null;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((fn) => fn());
}

/** Asks, and resolves true when the operator confirms. A second request while one is open declines the first. */
export function confirmAction(request: ConfirmRequest): Promise<boolean> {
  if (auto !== null) return Promise.resolve(auto);
  pending?.resolve(false);
  return new Promise((resolve) => {
    pending = { ...request, resolve };
    emit();
  });
}

export function answerConfirm(ok: boolean) {
  const p = pending;
  pending = null;
  emit();
  p?.resolve(ok);
}

export function getConfirm(): ConfirmRequest | null {
  return pending;
}

export function subscribeConfirm(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

/** For tests and stories: answer every request with `ok` without showing the dialog; null to show it again. */
export function setAutoConfirm(ok: boolean | null) {
  auto = ok;
}
