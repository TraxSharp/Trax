// The dashboard authenticates to the Trax GraphQL endpoint with either an API key or a Bearer
// (JWT) token. The credential and its mode are held in localStorage so they survive reloads.
// There is no login flow: an operator pastes a credential once (see ConnectionGate) and it is
// reused until cleared.
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

// Retain the original storage key so an existing API key survives an upgrade to this version.
const CRED_KEY = "trax-dashboard-api-key";
const MODE_KEY = "trax-dashboard-auth-mode";

// Small pub/sub so the urql client and React can react to credential changes without a full reload.
const listeners = new Set<() => void>();
function notify(): void {
  listeners.forEach((fn) => fn());
}

export function getCredential(): string {
  return localStorage.getItem(CRED_KEY) ?? "";
}

export function getAuthMode(): AuthMode {
  return localStorage.getItem(MODE_KEY) === "bearer" ? "bearer" : "apikey";
}

export function hasCredential(): boolean {
  return getCredential().length > 0;
}

export function setCredential(value: string, mode: AuthMode): void {
  localStorage.setItem(CRED_KEY, value.trim());
  localStorage.setItem(MODE_KEY, mode);
  notify();
}

export function clearCredential(): void {
  localStorage.removeItem(CRED_KEY);
  localStorage.removeItem(MODE_KEY);
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
