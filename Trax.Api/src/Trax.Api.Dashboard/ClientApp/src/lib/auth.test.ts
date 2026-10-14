import { afterEach, describe, expect, test, vi } from "vitest";
import {
  authConnectionParams,
  authHttpHeaders,
  clearCredential,
  getAuthMode,
  getCredential,
  hasCredential,
  onAuthChange,
  removeStoredCredentials,
  setCredential,
} from "./auth";

const storedKeys = () =>
  Array.from({ length: localStorage.length }, (_, i) => localStorage.key(i)).sort();

afterEach(() => {
  clearCredential();
  localStorage.clear();
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

describe("the credential is held in memory only", () => {
  test("setting, reading and clearing it never touches browser storage", () => {
    // Storage.prototype covers jsdom's storages; the test polyfill's localStorage (Node 26) is a
    // plain object, so its own methods are watched as well.
    const spies = [
      vi.spyOn(Storage.prototype, "setItem"),
      vi.spyOn(Storage.prototype, "getItem"),
      vi.spyOn(localStorage, "setItem"),
      vi.spyOn(localStorage, "getItem"),
    ];
    localStorage.clear();
    try {
      setCredential("admin-key-123", "apikey");
      setCredential("eyJhbGci.payload.sig", "bearer");
      expect(authHttpHeaders()).toEqual({ Authorization: "Bearer eyJhbGci.payload.sig" });
      expect(authConnectionParams()).toEqual({ authToken: "eyJhbGci.payload.sig" });
      clearCredential();

      spies.forEach((spy) => expect(spy).not.toHaveBeenCalled());
      expect(localStorage.length).toBe(0);
      expect(sessionStorage.length).toBe(0);
      expect(document.cookie).toBe("");
    } finally {
      spies.forEach((spy) => spy.mockRestore());
    }
  });

  test("a reload starts with no credential", async () => {
    setCredential("admin-key-123", "apikey");

    vi.resetModules();
    const reloaded = await import("./auth");
    reloaded.removeStoredCredentials();

    expect(reloaded.hasCredential()).toBe(false);
    expect(reloaded.authHttpHeaders()).toEqual({});
    expect(reloaded.authConnectionParams()).toEqual({});
  });
});

describe("what earlier versions stored is deleted at startup", () => {
  test("the shared legacy keys and every per-dashboard key go; other settings stay", () => {
    localStorage.clear();
    localStorage.setItem("trax-dashboard-api-key", "legacy-key");
    localStorage.setItem("trax-dashboard-auth-mode", "bearer");
    localStorage.setItem("trax-dashboard-credential-owner", "anonymous");
    localStorage.setItem(
      'trax-dashboard-credential:["/ops/","/ops/graphql"]',
      JSON.stringify({ owner: "anonymous", mode: "apikey", credential: "ops-key" }),
    );
    localStorage.setItem('trax-dashboard-credential:["/","/trax/graphql"]', "{not json");
    localStorage.setItem("trax-dashboard-theme", "dark");
    localStorage.setItem("trax:poll-seconds", "5");

    removeStoredCredentials();

    expect(storedKeys()).toEqual(["trax-dashboard-theme", "trax:poll-seconds"]);
    expect(hasCredential()).toBe(false);
  });

  test("storage that throws does not stop the app", () => {
    const key = vi.spyOn(localStorage, "key").mockImplementation(() => {
      throw new Error("SecurityError");
    });
    try {
      expect(() => removeStoredCredentials()).not.toThrow();
    } finally {
      key.mockRestore();
    }
  });
});
