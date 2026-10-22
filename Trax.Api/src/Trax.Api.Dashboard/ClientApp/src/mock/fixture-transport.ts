import type { TraxRequest, TraxTransport } from "../trax/index.g";
import { hashVariables } from "./variables-hash";
import { operationFixtures } from "./fixtures";

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
function lookup(request: TraxRequest): unknown {
  const byHash = operationFixtures[request.document.name];
  if (!byHash) return undefined;

  const vars = request.variables as Record<string, unknown>;
  const exact = byHash[hashVariables(vars)];
  if (exact !== undefined) return exact;

  if (vars.id != null || vars.parentId != null) return undefined;

  const base = Object.values(byHash)[0];
  if (base === undefined) return undefined;
  return vars.afterId != null ? terminatePages(base) : base;
}

/**
 * Fixture-first: replays the captured real-data snapshot for any query it has a fixture for, and
 * forwards everything else (unfixtured queries, mutations, subscriptions) to the next transport (the
 * auto-mock schema). Sits below the stateful layer, so overlay query merges still apply on top of
 * fixture data (read-after-write over real shapes).
 */
export function fixtureTransport(next: TraxTransport): TraxTransport {
  return {
    async execute(request) {
      const data = request.document.kind === "query" ? lookup(request) : undefined;
      return data !== undefined ? { data: structuredClone(data) } : next.execute(request);
    },
    subscribe: (request, sink) => next.subscribe(request, sink),
  };
}
