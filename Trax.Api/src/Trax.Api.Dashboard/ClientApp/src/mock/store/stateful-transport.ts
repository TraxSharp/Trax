import type { TraxTransport } from "../../trax/index.g";
import type { MockStore } from "./mock-store";
import {
  defaultOverlays,
  mockedSubscriptions,
  type MutationContext,
  type MutationOverlay,
  type QueryOverlay,
  type StatefulOverlay,
} from "./overlays";

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
 * Threads a {@link MockStore} delta through the documents an overlay knows about:
 *
 *   - overlay mutation   -> write the store, answer with the overlay's ACK (never forwarded).
 *   - mocked subscription -> emit store events published for that operation (never forwarded;
 *     the auto-mock schema cannot execute subscriptions).
 *   - overlay query      -> forward to the base (auto-mock/fixtures), then merge the delta.
 *   - everything else    -> forwarded untouched.
 *
 * `top` is the whole transport this layer sits in: a mutation overlay's read goes through it, so it
 * is answered as a page's is.
 */
export function statefulTransport(
  store: MockStore,
  overlays: StatefulOverlay[] = defaultOverlays,
  top: () => TraxTransport,
): (next: TraxTransport) => TraxTransport {
  const { mutations, queries } = mergeOverlays(overlays);
  const context: MutationContext = {
    query: async (document, variables) => (await top().execute({ document, variables })).data,
  };
  return (next) => ({
    async execute(request) {
      const { name, kind } = request.document;
      const write = kind === "mutation" ? mutations[name] : undefined;
      if (write) return { data: await write({ ...request.variables }, store, context) };
      const answer = await next.execute(request);
      const overlay = kind === "query" ? queries[name] : undefined;
      return overlay && answer.data != null
        ? { ...answer, data: overlay(answer.data, store, { ...request.variables }) }
        : answer;
    },
    subscribe(request, sink) {
      const name = request.document.name;
      if (!mockedSubscriptions.has(name)) return next.subscribe(request, sink);
      return store.subscribeEvents((eventName, data) => {
        if (eventName === name) sink.next({ data });
      });
    },
  });
}
