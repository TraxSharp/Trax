import { useSyncExternalStore } from "react";
import { connection, type ConnectionStatus } from "./graphql";

// Reflects the live graphql-ws socket state. The socket connects lazily when a page mounts
// a subscription (Overview, Executions), so pages without one report "closed".
export function useConnectionStatus(): ConnectionStatus {
  return useSyncExternalStore(
    connection.subscribe,
    connection.get,
    () => "closed" as const,
  );
}
