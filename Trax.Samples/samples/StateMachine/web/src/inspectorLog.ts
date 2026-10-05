import { useSyncExternalStore } from "react";

/**
 * A log of what the client actually sends and receives, for the "Under the hood" panel: every HTTP
 * GraphQL request and every WebSocket frame, each with a note on what Trax did. It lives outside React
 * because the Apollo links that feed it are created outside React.
 */

export type Channel = "http" | "ws";
export type Direction = "out" | "in" | "info";

export interface InspectorEntry {
  id: number;
  at: string;
  channel: Channel;
  direction: Direction;
  /** Who was signed in when the entry was recorded. */
  user: string;
  title: string;
  /** What Trax did, in a sentence, when the frame or request says. */
  note?: string;
  /** The Trax run this entry belongs to, when it names one. */
  runId?: string;
  /** Keep-alive traffic, hidden unless asked for. */
  quiet?: boolean;
  /** The request or frame itself, pretty-printed on demand. */
  detail?: unknown;
  durationMs?: number;
  failed?: boolean;
}

const LIMIT = 400;
let entries: InspectorEntry[] = [];
let nextId = 1;
const listeners = new Set<() => void>();

export function record(entry: Omit<InspectorEntry, "id" | "at">): void {
  entries = [...entries.slice(-(LIMIT - 1)), { ...entry, id: nextId++, at: new Date().toISOString() }];
  listeners.forEach((l) => l());
}

export function clearInspector(): void {
  entries = [];
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useInspector(): InspectorEntry[] {
  return useSyncExternalStore(subscribe, () => entries);
}

/** The key is a credential, demo or not: the log shows that one was sent, never its value. */
export function maskKey(key: string): string {
  return key.length <= 6 ? "•••" : `${key.slice(0, 6)}•••`;
}
