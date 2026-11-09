import { afterEach, describe, expect, it, vi } from "vitest";

// The page reads the host's origin from the build: localhost in development, another origin when it is served from
// one. The socket follows it.

afterEach(() => {
  vi.unstubAllEnvs();
  vi.resetModules();
});

describe("the host's origin", () => {
  it("is localhost:5280 when the build names none", async () => {
    vi.stubEnv("VITE_STATEMACHINE_API", undefined);
    vi.resetModules();
    const host = await import("./host");

    expect(host.ENDPOINT).toBe("http://localhost:5280/trax/graphql");
    expect(host.SOCKET).toBe("ws://localhost:5280/trax/graphql");
  });

  it("is the one the build names, with the socket over wss", async () => {
    vi.stubEnv("VITE_STATEMACHINE_API", "https://statemachine.example");
    vi.resetModules();
    const host = await import("./host");

    expect(host.ENDPOINT).toBe("https://statemachine.example/trax/graphql");
    expect(host.SOCKET).toBe("wss://statemachine.example/trax/graphql");
  });
});
