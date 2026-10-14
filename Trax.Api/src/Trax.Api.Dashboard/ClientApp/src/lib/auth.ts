// The dashboard authenticates to the Trax GraphQL endpoint with either an API key or a Bearer
// (JWT) token. The credential and its mode are held in localStorage so they survive reloads.
// There is no login flow: an operator pastes a credential once (see ConnectionGate) and it is
// reused until cleared, or until the page is loaded by someone else.
//
// The credential is bound to who loaded the page. The host writes an opaque fingerprint of the
// shell's signed-in principal ("anonymous" when nobody signed in) into the runtime config, and the
// credential is stored with the fingerprint it was entered under. A page whose fingerprint differs
// forgets it, so in a shared browser the next person to pass the shell's posture does not inherit
// the last one's key, and signing out of the shell drops it at the next load.
//
// The two modes map to different wire formats, and this module is the single source of truth
// for that mapping:
//
//   API key  → HTTP header  X-Api-Key: <key>          WS connection_init { apiKey: <key> }
//   Bearer   → HTTP header  Authorization: Bearer …   WS connection_init { authToken: <jwt> }
//
// Bearer mode lets the dashboard target a JWT-secured Trax API — for example one that uses
// TraxJwtSocketInterceptor / TraxJwtDispatcherSocketInterceptor to authenticate subscriptions.

import { readRuntimeConfig } from "./runtimeConfig";

export type AuthMode = "apikey" | "bearer";

const CRED_KEY = "trax-dashboard-api-key";
const MODE_KEY = "trax-dashboard-auth-mode";
// The fingerprint of the principal the credential was entered under. A credential stored without
// one (by an earlier version) is forgotten like any other.
const OWNER_KEY = "trax-dashboard-credential-owner";

// Small pub/sub so the urql client and React can react to credential changes without a full reload.
const listeners = new Set<() => void>();
function notify(): void {
  listeners.forEach((fn) => fn());
}

const currentPrincipal = (): string => readRuntimeConfig().principal;

function removeStored(): void {
  localStorage.removeItem(CRED_KEY);
  localStorage.removeItem(MODE_KEY);
  localStorage.removeItem(OWNER_KEY);
}

/**
 * Forgets a stored credential that was entered under another principal than the one this page was
 * loaded by. Called once at startup; every read checks again, so nothing can use it meanwhile.
 * Returns whether one was forgotten.
 */
export function forgetCredentialOfAnotherPrincipal(): boolean {
  if (localStorage.getItem(CRED_KEY) === null) return false;
  if (localStorage.getItem(OWNER_KEY) === currentPrincipal()) return false;
  removeStored();
  return true;
}

export function getCredential(): string {
  // No notify here: this runs inside React's getSnapshot, and the answer is "" either way.
  if (forgetCredentialOfAnotherPrincipal()) return "";
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
  localStorage.setItem(OWNER_KEY, currentPrincipal());
  notify();
}

export function clearCredential(): void {
  removeStored();
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
