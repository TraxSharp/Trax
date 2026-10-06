// A tiny, framework-free container for the GraphQL mock's mutable overlay: the per-session
// delta that mutations write on top of the immutable auto-mock/fixtures. Reads merge seed +
// delta so a screen shows a write immediately (read-after-write). Ported from an Apollo mock
// and trimmed for a browser SPA (the store runs in the tab, so inspection is just
// globalThis.__mockStore + a localStorage mirror, no cross-process plumbing).
//
// It also carries a subscription event bus: publishEvent fans data out to matching client
// subscriptions, which is how the offline demo fires realtime events with no backend.

export type MockStoreState = Record<string, unknown>;

export interface MockStoreFieldChange {
  key: string;
  before: unknown;
  after: unknown;
}

export interface MockStoreChange {
  action: string;
  at: number;
  changed: MockStoreFieldChange[];
}

export type MockStoreListener = (state: MockStoreState, action: string) => void;
export type MockStoreEventListener = (operationName: string, data: Record<string, unknown>) => void;

export interface MockStoreEvent {
  operationName: string;
  data: Record<string, unknown>;
  at: number;
}

export interface MockStore {
  getState(): MockStoreState;
  getHistory(): MockStoreChange[];
  update(action: string, mutate: (draft: MockStoreState) => void): MockStoreState;
  reset(): void;
  subscribe(listener: MockStoreListener): () => void;
  publishEvent(operationName: string, data: Record<string, unknown>): void;
  subscribeEvents(listener: MockStoreEventListener): () => void;
  getEvents(): MockStoreEvent[];
}

export interface MockStoreOptions {
  storageKey?: string;
  persist?: boolean;
  exposeOnWindow?: boolean;
  log?: boolean;
  historyLimit?: number;
}

const DEFAULT_STORAGE_KEY = "trax:mock-overlay";
const DEFAULT_HISTORY_LIMIT = 50;
const RESET_ACTION = "@@RESET";

const globals = globalThis as typeof globalThis & {
  __mockStore?: MockStore;
  localStorage?: {
    getItem(k: string): string | null;
    setItem(k: string, v: string): void;
    removeItem(k: string): void;
  };
};

function diffState(before: MockStoreState, after: MockStoreState): MockStoreFieldChange[] {
  const keys = new Set([...Object.keys(before), ...Object.keys(after)]);
  const changes: MockStoreFieldChange[] = [];
  for (const key of keys) {
    if (JSON.stringify(before[key]) !== JSON.stringify(after[key])) {
      changes.push({ key, before: before[key], after: after[key] });
    }
  }
  return changes;
}

function clone(value: MockStoreState): MockStoreState {
  return typeof structuredClone === "function" ? structuredClone(value) : { ...value };
}

function hydrate(storageKey: string, persist: boolean): MockStoreState {
  if (!persist) return {};
  try {
    const raw = globals.localStorage?.getItem(storageKey);
    return raw ? (JSON.parse(raw) as MockStoreState) : {};
  } catch {
    return {};
  }
}

export function createMockStore(options: MockStoreOptions = {}): MockStore {
  const {
    storageKey = DEFAULT_STORAGE_KEY,
    persist = true,
    exposeOnWindow = true,
    log = false,
    historyLimit = DEFAULT_HISTORY_LIMIT,
  } = options;

  let state: MockStoreState = hydrate(storageKey, persist);
  const listeners = new Set<MockStoreListener>();
  const eventListeners = new Set<MockStoreEventListener>();
  const history: MockStoreChange[] = [];
  const events: MockStoreEvent[] = [];

  function mirror() {
    if (!persist) return;
    try {
      globals.localStorage?.setItem(storageKey, JSON.stringify(state));
    } catch {
      /* private mode / quota — inspection is best-effort */
    }
  }

  function record(action: string, before: MockStoreState, after: MockStoreState) {
    const change: MockStoreChange = { action, at: Date.now(), changed: diffState(before, after) };
    history.push(change);
    if (history.length > historyLimit) history.shift();
    if (log && change.changed.length > 0) {
      const summary = change.changed
        .map((c) => `${c.key}: ${preview(c.before)} -> ${preview(c.after)}`)
        .join(", ");
      console.info(`[mock-overlay] ${action} — ${summary}`);
    }
  }

  function broadcast(action: string) {
    for (const l of listeners) l(state, action);
  }

  const store: MockStore = {
    getState: () => state,
    getHistory: () => [...history],
    update(action, mutate) {
      const previous = state;
      const draft = clone(state);
      mutate(draft);
      state = draft;
      record(action, previous, state);
      mirror();
      broadcast(action);
      return state;
    },
    reset() {
      state = {};
      history.length = 0;
      events.length = 0;
      if (persist) {
        try {
          globals.localStorage?.removeItem(storageKey);
        } catch {
          /* best-effort */
        }
      }
      broadcast(RESET_ACTION);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    publishEvent(operationName, data) {
      events.push({ operationName, data, at: Date.now() });
      if (events.length > historyLimit) events.shift();
      if (log) console.info(`[mock-overlay] event ${operationName}`);
      for (const l of eventListeners) l(operationName, data);
    },
    subscribeEvents(listener) {
      eventListeners.add(listener);
      return () => {
        eventListeners.delete(listener);
      };
    },
    getEvents: () => [...events],
  };

  if (exposeOnWindow) globals.__mockStore = store;
  return store;
}

function preview(value: unknown): string {
  const json = value === undefined ? "undefined" : JSON.stringify(value);
  return json && json.length > 120 ? `${json.slice(0, 117)}…` : String(json);
}
