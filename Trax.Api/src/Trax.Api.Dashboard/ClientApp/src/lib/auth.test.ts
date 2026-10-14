import { afterEach, describe, expect, test, vi } from "vitest";
import {
  authConnectionParams,
  authHttpHeaders,
  clearCredential,
  credentialStorageKey,
  CREDENTIAL_KEY_PREFIX,
  forgetCredentialOfAnotherPrincipal,
  getAuthMode,
  getCredential,
  hasCredential,
  onAuthChange,
  setCredential,
  UNIDENTIFIED_PRINCIPAL,
} from "./auth";

// The credential's keys in storage (the app's settings live under "trax:", not these).
const keys = () =>
  Array.from({ length: localStorage.length }, (_, i) => localStorage.key(i)).filter((k) =>
    k?.startsWith("trax-dashboard-"),
  );

function loadedBy(
  principal: string | null,
  where: { basePath?: string; graphqlPath?: string } = {},
) {
  document.head.querySelector('meta[name="trax-dashboard-config"]')?.remove();
  if (principal === null) return;
  const meta = document.createElement("meta");
  meta.name = "trax-dashboard-config";
  meta.content = JSON.stringify({
    basePath: where.basePath ?? "/",
    graphqlPath: where.graphqlPath ?? "/trax/graphql",
    principal,
  });
  document.head.appendChild(meta);
}

afterEach(() => {
  loadedBy(null);
  clearCredential();
});

test("defaults: no credential, api-key mode, empty wire format", () => {
  expect(hasCredential()).toBe(false);
  expect(getCredential()).toBe("");
  expect(getAuthMode()).toBe("apikey");
  expect(authHttpHeaders()).toEqual({});
  expect(authConnectionParams()).toEqual({});
});

test("api-key mode maps to X-Api-Key header and apiKey connection param", () => {
  setCredential("admin-key-123", "apikey");

  expect(hasCredential()).toBe(true);
  expect(getAuthMode()).toBe("apikey");
  expect(authHttpHeaders()).toEqual({ "X-Api-Key": "admin-key-123" });
  expect(authConnectionParams()).toEqual({ apiKey: "admin-key-123" });
});

test("bearer mode maps to Authorization: Bearer and authToken connection param", () => {
  setCredential("eyJhbGci.payload.sig", "bearer");

  expect(getAuthMode()).toBe("bearer");
  expect(authHttpHeaders()).toEqual({ Authorization: "Bearer eyJhbGci.payload.sig" });
  expect(authConnectionParams()).toEqual({ authToken: "eyJhbGci.payload.sig" });
});

test("switching modes updates both wire formats", () => {
  setCredential("k", "apikey");
  expect(authHttpHeaders()).toHaveProperty("X-Api-Key");
  setCredential("t", "bearer");
  expect(authHttpHeaders()).toHaveProperty("Authorization");
  expect(authHttpHeaders()).not.toHaveProperty("X-Api-Key");
});

test("credential is trimmed", () => {
  setCredential("  spaced-key  ", "apikey");
  expect(getCredential()).toBe("spaced-key");
  expect(authConnectionParams()).toEqual({ apiKey: "spaced-key" });
});

test("clear removes the credential and empties the wire format", () => {
  setCredential("k", "bearer");
  clearCredential();
  expect(hasCredential()).toBe(false);
  expect(getAuthMode()).toBe("apikey");
  expect(authHttpHeaders()).toEqual({});
  expect(authConnectionParams()).toEqual({});
});

test("onAuthChange notifies on set and clear", () => {
  const fn = vi.fn();
  const off = onAuthChange(fn);
  setCredential("k", "apikey");
  clearCredential();
  expect(fn).toHaveBeenCalledTimes(2);
  off();
  setCredential("k2", "apikey");
  expect(fn).toHaveBeenCalledTimes(2); // no longer subscribed
});

describe("the credential is bound to who loaded the page", () => {
  test("the same principal keeps the credential across loads", () => {
    loadedBy("ada-fingerprint");
    setCredential("ada-key", "apikey");

    loadedBy("ada-fingerprint");
    expect(forgetCredentialOfAnotherPrincipal()).toBe(false);
    expect(getCredential()).toBe("ada-key");
    expect(authHttpHeaders()).toEqual({ "X-Api-Key": "ada-key" });
  });

  test("a page loaded by someone else forgets the last person's credential", () => {
    loadedBy("ada-fingerprint");
    setCredential("ada-admin-key", "bearer");

    loadedBy("grace-fingerprint");
    expect(hasCredential()).toBe(false);
    expect(authHttpHeaders()).toEqual({});
    expect(authConnectionParams()).toEqual({});
    expect(getAuthMode()).toBe("apikey");

    // Gone for good, not hidden: Ada's next load does not bring it back.
    loadedBy("ada-fingerprint");
    expect(hasCredential()).toBe(false);
  });

  test("a page that loads as anonymous after a signed-in one drops the credential", () => {
    loadedBy("ada-fingerprint");
    setCredential("ada-key", "apikey");

    loadedBy("anonymous");
    expect(forgetCredentialOfAnotherPrincipal()).toBe(true);
    expect(keys()).toEqual([]);
  });

  test("a stored value that is not a credential is removed", () => {
    loadedBy("anonymous");
    localStorage.setItem(credentialStorageKey(), "{not json");

    expect(getCredential()).toBe("");
    expect(localStorage.getItem(credentialStorageKey())).toBeNull();
  });
});

describe("a principal the host could not identify", () => {
  test("keeps the credential in memory for the page and never stores it", () => {
    loadedBy(UNIDENTIFIED_PRINCIPAL);
    setCredential("key", "bearer");

    expect(getCredential()).toBe("key");
    expect(authHttpHeaders()).toEqual({ Authorization: "Bearer key" });
    expect(keys()).toEqual([]);
  });

  test("does not read a credential stored under another principal, and removes it", () => {
    loadedBy("anonymous");
    setCredential("anonymous-key", "apikey");

    loadedBy(UNIDENTIFIED_PRINCIPAL);
    expect(hasCredential()).toBe(false);
    expect(keys()).toEqual([]);
  });

  test("a page that names someone after it does not get the in-memory credential", () => {
    loadedBy(UNIDENTIFIED_PRINCIPAL);
    setCredential("key", "apikey");

    loadedBy("ada-fingerprint");
    expect(hasCredential()).toBe(false);
  });
});

describe("each dashboard keeps its own credential", () => {
  test("a credential entered on one dashboard is not sent to another's endpoint", () => {
    loadedBy("ada-fingerprint", { basePath: "/ops/", graphqlPath: "/ops/graphql" });
    setCredential("ops-key", "apikey");

    loadedBy("ada-fingerprint", { basePath: "/trax/dashboard/", graphqlPath: "/trax/graphql" });
    expect(hasCredential()).toBe(false);
    expect(authHttpHeaders()).toEqual({});
    setCredential("trax-key", "apikey");

    loadedBy("ada-fingerprint", { basePath: "/ops/", graphqlPath: "/ops/graphql" });
    expect(authHttpHeaders()).toEqual({ "X-Api-Key": "ops-key" });
  });

  test("two dashboards on the same base path but different endpoints keep apart", () => {
    loadedBy("anonymous", { basePath: "/d/", graphqlPath: "/a/graphql" });
    setCredential("a-key", "apikey");

    loadedBy("anonymous", { basePath: "/d/", graphqlPath: "/b/graphql" });
    expect(hasCredential()).toBe(false);
  });

  test("the storage key names the base path and the endpoint under the documented prefix", () => {
    loadedBy("anonymous", { basePath: "/ops/", graphqlPath: "/ops/graphql" });
    expect(credentialStorageKey()).toBe(`${CREDENTIAL_KEY_PREFIX}["/ops/","/ops/graphql"]`);
    expect(CREDENTIAL_KEY_PREFIX).toBe("trax-dashboard-credential:");
  });

  test("a credential stored under the old shared key is deleted, not used or moved", () => {
    loadedBy("anonymous");
    localStorage.setItem("trax-dashboard-api-key", "legacy-key");
    localStorage.setItem("trax-dashboard-auth-mode", "bearer");
    localStorage.setItem("trax-dashboard-credential-owner", "anonymous");

    expect(forgetCredentialOfAnotherPrincipal()).toBe(true);
    expect(getCredential()).toBe("");
    expect(getAuthMode()).toBe("apikey");
    expect(keys()).toEqual([]);
  });
});
