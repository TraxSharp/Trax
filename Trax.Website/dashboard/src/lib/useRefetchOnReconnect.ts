import { useEffect, useRef } from "react";
import { connection } from "./connection";

// Fires `refetch` when the live socket recovers: a transition into "connected" after it had already
// connected once. graphql-ws re-establishes active subscriptions automatically on reconnect but
// does NOT replay events missed while the socket was down, so a network-only refetch fills that
// gap. The initial connect does not fire (the page's own query already loaded the data), and a
// drop to "closed"/"connecting" does not fire.
export function useRefetchOnReconnect(refetch: () => void): void {
  const ref = useRef(refetch);
  useEffect(() => {
    ref.current = refetch;
  });

  useEffect(() => {
    let everConnected = connection.get() === "connected";
    let prev = connection.get();
    return connection.subscribe(() => {
      const now = connection.get();
      const recovered = prev !== "connected" && now === "connected";
      prev = now;
      if (now !== "connected") return;
      if (everConnected && recovered) ref.current();
      everConnected = true;
    });
  }, []);
}
