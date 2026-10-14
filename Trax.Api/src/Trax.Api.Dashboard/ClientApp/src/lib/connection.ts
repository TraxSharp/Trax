// Live graphql-ws socket status, surfaced in the UI so operators can tell whether the feed is
// actually connected, and used to refetch grids after a reconnect. Kept in its own module (no
// WebSocket client dependency) so it can be driven directly in tests.
export type ConnectionStatus = "connecting" | "connected" | "closed";

let status: ConnectionStatus = "closed";
const listeners = new Set<() => void>();

export function setConnectionStatus(next: ConnectionStatus): void {
  if (next === status) return;
  status = next;
  listeners.forEach((fn) => fn());
}

export const connection = {
  get: (): ConnectionStatus => status,
  subscribe: (fn: () => void): (() => void) => {
    listeners.add(fn);
    return () => {
      listeners.delete(fn);
    };
  },
};
