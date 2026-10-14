import type { Exchange, Operation } from "@urql/core";
import { makeResult } from "@urql/core";
import { filter, map, merge, pipe, share } from "wonka";
import { hashVariables } from "./variables-hash";
import { operationFixtures } from "./fixtures";

function operationName(op: Operation): string {
  for (const def of op.query.definitions) {
    if (def.kind === "OperationDefinition" && def.name) return def.name.value;
  }
  return "";
}

// Collapse every keyset page in a result to an empty terminal page, so a request carrying a
// cursor we never captured ends pagination cleanly instead of re-serving page 1.
function terminatePages(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(terminatePages);
  if (value && typeof value === "object") {
    const obj = value as Record<string, unknown>;
    if (Array.isArray(obj.items) && "nextCursor" in obj) {
      return { ...obj, items: [], nextCursor: null };
    }
    return Object.fromEntries(Object.entries(obj).map(([k, v]) => [k, terminatePages(v)]));
  }
  return value;
}

// Resolve a query to captured data, or undefined to forward it to auto-mock.
//   - exact variables match wins.
//   - a detail request (`id` / `parentId`) with no exact match forwards to auto-mock rather
//     than serving a different record's data.
//   - a list request with no exact match serves the first captured page (so a filtered view
//     still renders), terminated when it carries an uncaptured cursor.
function lookup(op: Operation): unknown {
  const byHash = operationFixtures[operationName(op)];
  if (!byHash) return undefined;

  const vars = (op.variables ?? {}) as Record<string, unknown>;
  const exact = byHash[hashVariables(vars)];
  if (exact !== undefined) return exact;

  if (vars.id != null || vars.parentId != null) return undefined;

  const base = Object.values(byHash)[0];
  if (base === undefined) return undefined;
  return vars.afterId != null ? terminatePages(base) : base;
}

/**
 * Fixture-first exchange: replays the captured real-data snapshot for any query it has a
 * fixture for, and forwards everything else (unfixtured queries, mutations, subscriptions) to
 * the next exchange (the auto-mock schema). Sits below the stateful exchange, so overlay query
 * merges still apply on top of fixture data (read-after-write over real shapes).
 */
export function fixtureExchange(): Exchange {
  return ({ forward }) =>
    (ops$) => {
      const shared$ = pipe(ops$, share);
      const isFixtured = (op: Operation) => op.kind === "query" && lookup(op) !== undefined;

      const fixtured$ = pipe(
        shared$,
        filter(isFixtured),
        map((op) =>
          makeResult(op, { data: structuredClone(lookup(op)) as Record<string, unknown> }),
        ),
      );

      const forwarded$ = pipe(
        shared$,
        filter((op) => !isFixtured(op)),
        forward,
      );

      return merge([fixtured$, forwarded$]);
    };
}
