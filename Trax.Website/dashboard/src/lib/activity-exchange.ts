import type { Exchange } from "@urql/core";
import { pipe, tap } from "wonka";
import { setActiveCount } from "./activity";
import { noteQueryResult } from "./refreshStatus";

// urql exchange that tracks in-flight query + mutation operations so a global progress indicator
// can show whenever the app is talking to the API. Keyed by operation, so re-emitting queries
// (cache-and-network) and duplicate results don't double-count. Subscriptions are excluded (they
// are long-lived and would pin the indicator on). Place it first in the exchange list so it sees
// every operation before the cache short-circuits any.
export const activityExchange: Exchange =
  ({ forward }) =>
  (ops$) => {
    const inflight = new Set<number>();
    const update = () => setActiveCount(inflight.size);
    return pipe(
      ops$,
      tap((op) => {
        if (op.kind === "query" || op.kind === "mutation") {
          inflight.add(op.key);
          update();
        } else if (op.kind === "teardown" && inflight.delete(op.key)) {
          update();
        }
      }),
      forward,
      tap((result) => {
        const { kind } = result.operation;
        // The header marks a refresh failed when the latest query failed, until one succeeds. A
        // probe whose failure is an answer (persisted operations not enabled) opts out.
        if (kind === "query" && !result.stale && !result.operation.context.ignoreRefreshStatus)
          noteQueryResult(result.error ? result.error.message : null);
        // A cache-and-network query emits a stale cached result first, then the network result;
        // only clear on the final (non-stale, non-deferred) result.
        if (
          (kind === "query" || kind === "mutation") &&
          !result.stale &&
          !result.hasNext &&
          inflight.delete(result.operation.key)
        )
          update();
      }),
    );
  };
