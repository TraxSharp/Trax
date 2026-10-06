import type { Exchange, Operation, OperationResult } from "@urql/core";
import { makeResult } from "@urql/core";
import { filter, make, map, merge, mergeMap, pipe, share, takeUntil, fromValue } from "wonka";
import type { MockStore } from "./mock-store";
import {
  defaultOverlays,
  mockedSubscriptions,
  type MutationOverlay,
  type QueryOverlay,
  type StatefulOverlay,
} from "./overlays";

function operationName(op: Operation): string {
  for (const def of op.query.definitions) {
    if (def.kind === "OperationDefinition" && def.name) return def.name.value;
  }
  return "";
}

function mergeOverlays(overlays: StatefulOverlay[]) {
  const mutations: Record<string, MutationOverlay> = {};
  const queries: Record<string, QueryOverlay> = {};
  for (const o of overlays) {
    Object.assign(mutations, o.mutations);
    Object.assign(queries, o.queries);
  }
  return { mutations, queries };
}

/**
 * Threads a {@link MockStore} delta through the operations an overlay knows about:
 *
 *   - overlay mutation  -> write the store, return the overlay's ACK (never forwarded).
 *   - mocked subscription -> emit store events published for that op name (never forwarded;
 *     the auto-mock schema cannot execute subscriptions).
 *   - overlay query      -> forward to the base (auto-mock/fixtures), then merge the delta.
 *   - everything else    -> forwarded untouched.
 */
export function statefulExchange(
  store: MockStore,
  overlays: StatefulOverlay[] = defaultOverlays,
): Exchange {
  const { mutations, queries } = mergeOverlays(overlays);

  const handles = (op: Operation): boolean =>
    (op.kind === "mutation" && Boolean(mutations[operationName(op)])) ||
    (op.kind === "subscription" && mockedSubscriptions.has(operationName(op)));

  return ({ forward }) =>
    (ops$) => {
      const shared$ = pipe(ops$, share);

      const handled$ = pipe(
        shared$,
        filter((op) => op.kind !== "teardown" && handles(op)),
        mergeMap((op) => {
          if (op.kind === "mutation") {
            const data = mutations[operationName(op)](op.variables ?? {}, store);
            return fromValue(makeResult(op, { data }));
          }
          // Subscription: a long-lived source of store events matching this op name,
          // completed when its teardown arrives.
          const name = operationName(op);
          const teardown$ = pipe(
            shared$,
            filter((o) => o.kind === "teardown" && o.key === op.key),
          );
          const events$ = make<OperationResult>((observer) => {
            return store.subscribeEvents((eventName, data) => {
              if (eventName === name) observer.next(makeResult(op, { data }));
            });
          });
          return pipe(events$, takeUntil(teardown$));
        }),
      );

      const forwarded$ = pipe(
        shared$,
        filter((op) => op.kind === "teardown" || !handles(op)),
        forward,
        map((result): OperationResult => {
          const op = result.operation;
          const overlay = op.kind === "query" ? queries[operationName(op)] : undefined;
          if (overlay && result.data != null) {
            return { ...result, data: overlay(result.data, store, op.variables ?? {}) };
          }
          return result;
        }),
      );

      return merge([handled$, forwarded$]);
    };
}
