import {
  Client,
  cacheExchange,
  fetchExchange,
  subscriptionExchange,
} from "@urql/core";
import { createClient as createWsClient } from "graphql-ws";
import { executeExchange } from "@urql/exchange-execute";
import { activityExchange } from "../lib/activity-exchange";
import { buildMockSchema, type MockSchemaOverrides } from "./build-mock-schema";
import { createMockStore, type MockStore } from "./store/mock-store";
import { statefulExchange } from "./store/stateful-exchange";
import { fixtureExchange } from "./fixture-exchange";
import { defaultOverlays, type StatefulOverlay } from "./store/overlays";

export interface MockClientOptions {
  /** Auto-mock overrides (per-type mocks / field resolvers) to shape a story's data. */
  overrides?: MockSchemaOverrides;
  /** Stateful overlays. Defaults to {@link defaultOverlays}. */
  overlays?: StatefulOverlay[];
  /** Inject a store (e.g. to share it with the /dev/mock-overlay viewer or assert in a test). */
  store?: MockStore;
  /**
   * Replay captured real-data fixtures for reads (fixture-first, auto-mock fallback). On by
   * default so the running app looks real offline. Stories set this false to stay pinned to
   * their own `overrides` and deterministic.
   */
  fixtures?: boolean;
}

/**
 * A urql Client backed entirely by the in-process mock: overlay mutations write a session
 * delta, overlay queries merge it back (read-after-write), mocked subscriptions replay store
 * events, and everything else auto-mocks from the SDL. No backend required.
 */
export function createMockClient(opts: MockClientOptions = {}): Client {
  const store = opts.store ?? createMockStore();
  const useFixtures = opts.fixtures ?? true;
  return new Client({
    url: "/mock", // never hit; executeExchange terminates every operation
    requestPolicy: "cache-and-network",
    exchanges: [
      activityExchange,
      cacheExchange,
      statefulExchange(store, opts.overlays ?? defaultOverlays),
      ...(useFixtures ? [fixtureExchange()] : []),
      executeExchange({ schema: buildMockSchema(opts.overrides) }),
    ],
  });
}

// Defaults for the Storybook e2e toggle: hit the devhost directly (Storybook has no Vite
// /trax proxy, so use absolute URLs and the admin key).
const DEV_HTTP = "http://localhost:5310/trax/graphql";
const DEV_WS = "ws://localhost:5310/trax/graphql";
const DEV_KEY = "admin-key-do-not-use-in-production";

export interface RealClientOptions {
  url?: string;
  wsUrl?: string;
  apiKey?: string;
}

/** A real urql Client pointed at a running Trax API. Used by the "real" Storybook path. */
export function createRealClient(opts: RealClientOptions = {}): Client {
  const url = opts.url ?? DEV_HTTP;
  const wsUrl = opts.wsUrl ?? DEV_WS;
  const apiKey = opts.apiKey ?? DEV_KEY;

  const wsClient = createWsClient({
    url: wsUrl,
    connectionParams: () => ({ apiKey }),
  });

  return new Client({
    url,
    requestPolicy: "cache-and-network",
    exchanges: [
      activityExchange,
      cacheExchange,
      fetchExchange,
      subscriptionExchange({
        forwardSubscription(request) {
          const input = { ...request, query: request.query ?? "" };
          return {
            subscribe(sink) {
              const unsubscribe = wsClient.subscribe(input, sink);
              return { unsubscribe };
            },
          };
        },
      }),
    ],
    fetchOptions: () => ({ headers: { "X-Api-Key": apiKey } }),
  });
}
