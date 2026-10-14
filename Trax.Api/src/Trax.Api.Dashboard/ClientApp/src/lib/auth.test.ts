import { afterEach, describe, expect, test, vi } from "vitest";
import {
  authConnectionParams,
  authHttpHeaders,
  clearCredential,
  forgetCredentialOfAnotherPrincipal,
  getAuthMode,
  getCredential,
  hasCredential,
  onAuthChange,
  setCredential,
} from "./auth";

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
  function loadedBy(principal: string | null) {
    document.head.querySelector('meta[name="trax-dashboard-config"]')?.remove();
    if (principal === null) return;
    const meta = document.createElement("meta");
    meta.name = "trax-dashboard-config";
    meta.content = JSON.stringify({ basePath: "/", graphqlPath: "/trax/graphql", principal });
    document.head.appendChild(meta);
  }

  afterEach(() => loadedBy(null));

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

  test("signing out of the shell (the page now loads anonymous) drops the credential", () => {
    loadedBy("ada-fingerprint");
    setCredential("ada-key", "apikey");

    loadedBy("anonymous");
    expect(forgetCredentialOfAnotherPrincipal()).toBe(true);
    expect(localStorage.getItem("trax-dashboard-api-key")).toBeNull();
  });

  test("a credential stored before it was bound to anyone is forgotten", () => {
    loadedBy("anonymous");
    localStorage.setItem("trax-dashboard-api-key", "legacy-key");
    localStorage.setItem("trax-dashboard-auth-mode", "apikey");

    expect(getCredential()).toBe("");
    expect(localStorage.getItem("trax-dashboard-api-key")).toBeNull();
  });
});
