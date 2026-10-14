import { useEffect, useRef } from "react";
import { useSubscription } from "urql";
import { ON_DATA_CHANGED } from "../graphql/subscriptions";
import { useRefetchOnReconnect } from "./useRefetchOnReconnect";

// GraphQL enum values (HotChocolate serializes ChangeDomain as CONSTANT_CASE).
export type ChangeDomain =
  | "WORK_QUEUE"
  | "DEAD_LETTER"
  | "MANIFEST"
  | "MANIFEST_GROUP"
  | "SCHEDULER_CONFIG"
  | "EXECUTION";

interface DataChangedEvent {
  domain: ChangeDomain;
  timestamp: string;
}

const DEBOUNCE_MS = 300;

// Push-based refetch. Subscribes to onDataChanged and, when one of the given domains changes,
// refetches after a short debounce so the grid updates without a poll timer. A burst of writes to
// the same domain coalesces server-side into one signal and again here into one refetch.
//
// The reducer keeps only matching events, so unrelated domains never touch `data` and thus never
// cancel a pending refetch. urql shares one WebSocket operation across every subscriber of the same
// document, so many pages using this hook cost a single subscription.
export function useRefetchOnChange(
  domains: ChangeDomain | ChangeDomain[],
  refetch: () => void,
): void {
  const wanted = Array.isArray(domains) ? domains : [domains];

  const [{ data }] = useSubscription<
    { onDataChanged: DataChangedEvent },
    DataChangedEvent
  >({ query: ON_DATA_CHANGED }, (prev, next) =>
    wanted.includes(next.onDataChanged.domain) ? next.onDataChanged : (prev as DataChangedEvent),
  );

  // Also refetch when the socket recovers: a signal emitted while it was down was never delivered,
  // so the grid would otherwise stay stale until the next navigation or poll.
  useRefetchOnReconnect(refetch);

  // Hold the latest refetch in a ref so the debounce effect re-runs on a new event, not on every
  // render (refetch is a fresh closure each render).
  const ref = useRef(refetch);
  useEffect(() => {
    ref.current = refetch;
  });

  useEffect(() => {
    if (!data) return;
    const t = setTimeout(() => ref.current(), DEBOUNCE_MS);
    return () => clearTimeout(t);
  }, [data]);
}
