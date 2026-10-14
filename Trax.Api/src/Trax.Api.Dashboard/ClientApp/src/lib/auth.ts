// The dashboard authenticates to the Trax GraphQL endpoint with either an API key or a Bearer
// (JWT) token. The credential and its mode are held in localStorage so they survive reloads.
// There is no login flow: an operator pastes a credential once (see ConnectionGate) and it is
// reused until cleared, or until the page is loaded by someone else.
//
// The credential belongs to one dashboard and one endpoint. It is stored under a key named after
// the page's base path and GraphQL path (credentialStorageKey), so two dashboards a host maps on
// the same origin never send each other's credential to their endpoints. A credential stored by an
// earlier version under the one shared key is deleted, not moved: nothing says which endpoint it
// was for.
//
// The credential is bound to who loaded the page. The host writes an opaque fingerprint of the
// shell's signed-in principal ("anonymous" when nobody signed in) into the runtime config, and the
// credential is stored with the fingerprint it was entered under. A page whose fingerprint differs
// forgets it, so in a shared browser the next person to pass the shell's posture does not inherit
// the last one's key. When the host could tell that someone signed in but not who
// (UNIDENTIFIED_PRINCIPAL), the credential is kept in memory for the life of the page and never
// written to storage.
//
// Signing out of the shell does not by itself remove a stored credential: the posture refuses the
// next load, so none of this code runs. A host clears it on its own sign-out path (see the
// UseTraxApiDashboard docs, "How the app authenticates").
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

/** Every stored credential's localStorage key starts with this; the docs name it for hosts. */
export const CREDENTIAL_KEY_PREFIX = "trax-dashboard-credential:";

/** The fingerprint of a page loaded by someone the host could not identify (DashboardEndpoint). */
export const UNIDENTIFIED_PRINCIPAL = "unidentified";

// The keys an earlier version stored one credential under, shared by every dashboard on the origin.
const LEGACY_KEYS = [
  "trax-dashboard-api-key",
  "trax-dashboard-auth-mode",
  "trax-dashboard-credential-owner",
];

interface Stored {
  /** The fingerprint of the principal the credential was entered under. */
  owner: string;
  mode: AuthMode;
  credential: string;
}

// The credential of a page whose principal is unidentified: this page's alone, never stored.
let inMemory: Stored | null = null;

// Small pub/sub so the urql client and React can react to credential changes without a full reload.
const listeners = new Set<() => void>();
function notify(): void {
  listeners.forEach((fn) => fn());
}

/** The localStorage key of this page's credential: its dashboard's base path and GraphQL path. */
export function credentialStorageKey(): string {
  const { basePath, graphqlPath } = readRuntimeConfig();
  return `${CREDENTIAL_KEY_PREFIX}${JSON.stringify([basePath, graphqlPath])}`;
}

const currentPrincipal = (): string => readRuntimeConfig().principal;

function parse(raw: string | null): Stored | null {
  if (raw === null) return null;
  try {
    const value = JSON.parse(raw) as Partial<Stored> | null;
    if (
      value &&
      typeof value.owner === "string" &&
      (value.mode === "apikey" || value.mode === "bearer") &&
      typeof value.credential === "string"
    )
      return { owner: value.owner, mode: value.mode, credential: value.credential };
  } catch {
    /* unreadable: treated as nothing stored, and removed */
  }
  return null;
}

function removeLegacy(): boolean {
  const had = localStorage.getItem(LEGACY_KEYS[0]) !== null;
  LEGACY_KEYS.forEach((key) => localStorage.removeItem(key));
  return had;
}

/**
 * Forgets a credential this page must not use: one stored by an earlier version under the shared
 * key, one entered under another principal than the one this page was loaded by, and any stored
 * one when the page's principal is unidentified. Called once at startup; every read checks again,
 * so nothing can use it meanwhile. Returns whether one was forgotten.
 */
export function forgetCredentialOfAnotherPrincipal(): boolean {
  let forgot = removeLegacy();
  const key = credentialStorageKey();
  const raw = localStorage.getItem(key);
  if (raw === null) return forgot;
  const stored = parse(raw);
  const principal = currentPrincipal();
  if (stored === null || principal === UNIDENTIFIED_PRINCIPAL || stored.owner !== principal) {
    localStorage.removeItem(key);
    forgot = true;
  }
  return forgot;
}

function current(): Stored | null {
  forgetCredentialOfAnotherPrincipal();
  if (currentPrincipal() === UNIDENTIFIED_PRINCIPAL) return inMemory;
  return parse(localStorage.getItem(credentialStorageKey()));
}

export function getCredential(): string {
  // No notify here: this runs inside React's getSnapshot, and the answer is "" either way.
  return current()?.credential ?? "";
}

export function getAuthMode(): AuthMode {
  return current()?.mode ?? "apikey";
}

export function hasCredential(): boolean {
  return getCredential().length > 0;
}

export function setCredential(value: string, mode: AuthMode): void {
  const stored: Stored = { owner: currentPrincipal(), mode, credential: value.trim() };
  if (stored.owner === UNIDENTIFIED_PRINCIPAL) inMemory = stored;
  else localStorage.setItem(credentialStorageKey(), JSON.stringify(stored));
  notify();
}

export function clearCredential(): void {
  inMemory = null;
  removeLegacy();
  localStorage.removeItem(credentialStorageKey());
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
