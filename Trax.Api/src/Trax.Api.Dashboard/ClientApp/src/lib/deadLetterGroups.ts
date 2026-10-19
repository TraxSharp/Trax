import type { DeadLetterSummary } from "../types";

// The Dead letters view groups by train and reason (api/0043): a train that keeps failing the same
// way is one problem to look at, however many manifests or retries it took to show it.

/** Dead letters of one train that were dead-lettered for the same reason. */
export interface DeadLetterGroup {
  key: string;
  train: string;
  reason: string;
  rows: DeadLetterSummary[];
  awaiting: number;
  /** Each manifest in the group once, newest first. */
  manifests: { id: number; externalId: string | null | undefined }[];
  latest: string;
}

/** Groups dead letters by train and reason, keeping the order they came in (newest first). */
export function groupDeadLetters(items: DeadLetterSummary[]): DeadLetterGroup[] {
  const groups = new Map<string, DeadLetterGroup>();
  for (const d of items) {
    const key = JSON.stringify([d.manifestName, d.reason]);
    let g = groups.get(key);
    if (!g) {
      g = { key, train: d.manifestName, reason: d.reason, rows: [], awaiting: 0, manifests: [], latest: d.deadLetteredAt };
      groups.set(key, g);
    }
    g.rows.push(d);
    if (d.status === "AWAITING_INTERVENTION") g.awaiting += 1;
    if (!g.manifests.some((m) => m.id === d.manifestId)) g.manifests.push({ id: d.manifestId, externalId: d.manifestExternalId });
    if (d.deadLetteredAt > g.latest) g.latest = d.deadLetteredAt;
  }
  return [...groups.values()];
}

