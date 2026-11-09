import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TraxOperationError } from "./trax/index.g";
import { signIn, signOutOnRefusal, signedIn, whenSignedOut } from "./session";

// Sign-in posts to the host's Development-only endpoint. Outside Development the endpoint is not mapped, and the
// host's answer has no JSON body to read.

beforeEach(() => vi.stubGlobal("sessionStorage", memoryStorage()));

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

function memoryStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() {
      return items.size;
    },
    clear: () => items.clear(),
    getItem: (key) => items.get(key) ?? null,
    key: (index) => [...items.keys()][index] ?? null,
    removeItem: (key) => void items.delete(key),
    setItem: (key, value) => void items.set(key, value),
  };
}

/** A token shaped as the host's: only its payload is read, so the signature is a placeholder. */
function token(exp: number): string {
  const part = (value: object) => btoa(JSON.stringify(value)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${part({ alg: "HS256", typ: "JWT" })}.${part({ sub: "Ada", exp })}.signature`;
}

function answersToken(value: string) {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response(JSON.stringify({ token: value }), { status: 200, headers: { "Content-Type": "application/json" } }),
  );
}

describe("signing in", () => {
  it("names the status of an answer with no body", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 404 }));

    await expect(signIn("Ada", "customer")).rejects.toThrow("Sign-in failed (404).");
  });

  it("names the status of an answer that is not JSON", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("<h1>Internal Server Error</h1>", { status: 500, headers: { "Content-Type": "text/html" } }),
    );

    await expect(signIn("Ada", "customer")).rejects.toThrow("Sign-in failed (500).");
  });

  it("gives the host's own reason when it sends one", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ error: "The role is not one of customer or baker." }), {
        status: 400,
        headers: { "Content-Type": "application/json" },
      }),
    );

    await expect(signIn("Ada", "customer")).rejects.toThrow("The role is not one of customer or baker.");
  });
});

// The host's token lasts an hour. A page left open past it, or opened again from the same tab, holds a token the host
// no longer takes.

describe("a stored sign-in", () => {
  it("is restored while its token is good", async () => {
    vi.useFakeTimers({ now: Date.UTC(2026, 10, 7, 12, 0, 0) });
    answersToken(token(Date.UTC(2026, 10, 7, 13, 0, 0) / 1000));
    await signIn("Ada", "customer");

    expect(signedIn()?.name).toBe("Ada");
  });

  it("is dropped once its token has expired", async () => {
    vi.useFakeTimers({ now: Date.UTC(2026, 10, 7, 12, 0, 0) });
    answersToken(token(Date.UTC(2026, 10, 7, 13, 0, 0) / 1000));
    await signIn("Ada", "customer");

    vi.setSystemTime(Date.UTC(2026, 10, 7, 13, 0, 1));
    expect(signedIn()).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });

  it("is dropped when its token has no expiry to read", () => {
    sessionStorage.setItem("trax.bakery.person", JSON.stringify({ name: "Ada", role: "customer", token: "not-a-jwt" }));

    expect(signedIn()).toBeNull();
  });
});

describe("a read the host refuses", () => {
  it("signs the person out when the host no longer knows them", async () => {
    answersToken(token(Date.now() / 1000 + 3600));
    await signIn("Ada", "customer");
    const signedOut = vi.fn();
    const stop = whenSignedOut(signedOut);

    expect(signOutOnRefusal(new TraxOperationError("UNAUTHENTICATED", "Sign in to read this."))).toBe(true);
    expect(signedIn()).toBeNull();
    expect(signedOut).toHaveBeenCalledOnce();
    stop();
  });

  it("keeps the person signed in for any other refusal", async () => {
    answersToken(token(Date.now() / 1000 + 3600));
    await signIn("Ada", "customer");
    const signedOut = vi.fn();
    const stop = whenSignedOut(signedOut);

    expect(signOutOnRefusal(new TraxOperationError("FORBIDDEN", "Not yours."))).toBe(false);
    expect(signOutOnRefusal(new Error("The host answered 502 Bad Gateway."))).toBe(false);
    expect(signedIn()?.name).toBe("Ada");
    expect(signedOut).not.toHaveBeenCalled();
    stop();
  });
});

// The token travels on every request. A build pointed at a host that is not loopback over http must be refused by the
// client before anything is sent, so the page never lifts the client's cleartext refusal.

describe("connecting", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("refuses cleartext to a host that is not loopback", async () => {
    vi.stubEnv("VITE_BAKERY_API", "http://bakery.example:5282");
    vi.resetModules();
    const session = await import("./session");
    const client = await import("./trax/index.g");
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("{}"));

    session.connect({ name: "Ada", role: "customer", token: token(Date.now() / 1000 + 3600) });

    await expect(client.readShopSignView()).rejects.toThrow(/cleartext/);
    expect(fetch).not.toHaveBeenCalled();
  });
});
