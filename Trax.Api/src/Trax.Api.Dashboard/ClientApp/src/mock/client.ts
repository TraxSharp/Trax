import { httpTransport, type TraxTransport } from "../trax/index.g";
import { ConsoleClient } from "../lib/consoleClient";
import { buildMockSchema, type MockSchemaOverrides } from "./build-mock-schema";
import { createMockStore, type MockStore } from "./store/mock-store";
import { statefulTransport } from "./store/stateful-transport";
import { fixtureTransport } from "./fixture-transport";
import { schemaTransport } from "./schema-transport";
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

/** The mock's transport alone: what createMockClient puts under its client. */
export function createMockTransport(opts: MockClientOptions = {}): TraxTransport {
  const store = opts.store ?? createMockStore();
  const useFixtures = opts.fixtures ?? true;
  const base = schemaTransport(buildMockSchema(opts.overrides));
  const stateful = statefulTransport(store, opts.overlays ?? defaultOverlays, () => top);
  const top: TraxTransport = stateful(useFixtures ? fixtureTransport(base) : base);
  return top;
}

/**
 * A console client backed entirely by the in-process mock: overlay mutations write a session
 * delta, overlay queries merge it back (read-after-write), mocked subscriptions replay store
 * events, and everything else auto-mocks from the SDL. No backend required.
 */
export function createMockClient(opts: MockClientOptions = {}): ConsoleClient {
  return new ConsoleClient(createMockTransport(opts));
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

/** A real client pointed at a running Trax API, sending documents as text. Used by the "real" Storybook path. */
export function createRealClient(opts: RealClientOptions = {}): ConsoleClient {
  const apiKey = opts.apiKey ?? DEV_KEY;
  return new ConsoleClient(
    httpTransport({
      url: opts.url ?? DEV_HTTP,
      socketUrl: opts.wsUrl ?? DEV_WS,
      headers: () => ({ "X-Api-Key": apiKey }),
      connectionParams: () => ({ apiKey }),
      byId: () => false,
    }),
  );
}
