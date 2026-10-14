// The dashboard authenticates to the Trax GraphQL endpoint with either an API key or a Bearer
// (JWT) token. There is no login flow: an operator pastes a credential (see ConnectionGate) and
// it is used until they change it or the page goes away.
//
// The credential and its mode are held in memory only, in this module, for the life of the page.
// They are never written to localStorage, sessionStorage, IndexedDB or a cookie, so nothing on the
// origin can read them later: a reload, a new tab or the next person at a shared browser starts
// with no credential and is asked for one again. Closing the tab or signing out of the shell
// leaves nothing behind.
//
// Earlier versions stored the credential in localStorage. removeStoredCredentials deletes what
// they left, and runs once at startup (main.tsx).
//
// The two modes map to different wire formats, and this module is the single source of truth
// for that mapping:
//
//   API key  → HTTP header  X-Api-Key: <key>          WS connection_init { apiKey: <key> }
//   Bearer   → HTTP header  Authorization: Bearer …   WS connection_init { authToken: <jwt> }
//
// Bearer mode lets the dashboard target a JWT-secured Trax API — for example one that uses
// TraxJwtSocketInterceptor / TraxJwtDispatcherSocketInterceptor to authenticate subscriptions.

export type AuthMode = "apikey" | "bearer";

// What earlier versions stored: one credential under these shared keys, then one per dashboard
// under a key starting with the prefix.
const LEGACY_KEYS = [
  "trax-dashboard-api-key",
  "trax-dashboard-auth-mode",
  "trax-dashboard-credential-owner",
];
const LEGACY_KEY_PREFIX = "trax-dashboard-credential:";

interface Held {
  mode: AuthMode;
  credential: string;
}

// This page's credential. Nothing else holds it.
let held: Held | null = null;

// Small pub/sub so the urql client and React can react to credential changes without a full reload.
const listeners = new Set<() => void>();
function notify(): void {
  listeners.forEach((fn) => fn());
}

/**
 * Deletes every credential an earlier version stored in localStorage: the shared legacy keys and
 * every key starting with "trax-dashboard-credential:". Storage that is missing or throws (a
 * private window, blocked site data) is left alone: then there is nothing to delete.
 */
export function removeStoredCredentials(): void {
  try {
    const storage = globalThis.localStorage;
    if (!storage) return;
    const stale = new Set(LEGACY_KEYS);
    for (let i = 0; i < storage.length; i++) {
      const key = storage.key(i);
      if (key?.startsWith(LEGACY_KEY_PREFIX)) stale.add(key);
    }
    stale.forEach((key) => storage.removeItem(key));
  } catch {
    /* storage unavailable: nothing was stored there */
  }
}

export function getCredential(): string {
  return held?.credential ?? "";
}

export function getAuthMode(): AuthMode {
  return held?.mode ?? "apikey";
}

export function hasCredential(): boolean {
  return getCredential().length > 0;
}

export function setCredential(value: string, mode: AuthMode): void {
  held = { mode, credential: value.trim() };
  notify();
}

export function clearCredential(): void {
  held = null;
  notify();
}

export function onAuthChange(fn: () => void): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

/** HTTP auth header for the current credential + mode. Empty when no credential is set. */
export function authHttpHeaders(): Record<string, string> {
  const cred = getCredential();
  if (!cred) return {};
  return getAuthMode() === "bearer"
    ? { Authorization: `Bearer ${cred}` }
    : { "X-Api-Key": cred };
}

/** graphql-ws connection_init payload for the current credential + mode. */
export function authConnectionParams(): Record<string, string> {
  const cred = getCredential();
  if (!cred) return {};
  return getAuthMode() === "bearer" ? { authToken: cred } : { apiKey: cred };
}
