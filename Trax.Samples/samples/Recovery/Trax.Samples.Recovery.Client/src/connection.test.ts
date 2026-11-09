import { afterEach, describe, expect, it, vi } from "vitest";

// The page reads the host's origin from the build: localhost in development, another origin when it is served from
// one. The socket and the dashboard link follow it.

afterEach(() => {
  vi.unstubAllEnvs();
  vi.resetModules();
});

describe("the host's origin", () => {
  it("is localhost:5260 when the build names none", async () => {
    vi.stubEnv("VITE_RECOVERY_API", undefined);
    vi.resetModules();
    const connection = await import("./connection");

    expect(connection.API_URL).toBe("http://localhost:5260/trax/graphql");
    expect(connection.SOCKET_URL).toBe("ws://localhost:5260/trax/graphql");
    expect(connection.DASHBOARD_URL).toBe("http://localhost:5260/trax");
  });

  it("is the one the build names, with the socket over wss", async () => {
    vi.stubEnv("VITE_RECOVERY_API", "https://recovery.example");
    vi.resetModules();
    const connection = await import("./connection");

    expect(connection.API_URL).toBe("https://recovery.example/trax/graphql");
    expect(connection.SOCKET_URL).toBe("wss://recovery.example/trax/graphql");
    expect(connection.DASHBOARD_URL).toBe("https://recovery.example/trax");
    expect(connection.connection()).toMatchObject({
      url: "https://recovery.example/trax/graphql",
      socketUrl: "wss://recovery.example/trax/graphql",
    });
  });
});
