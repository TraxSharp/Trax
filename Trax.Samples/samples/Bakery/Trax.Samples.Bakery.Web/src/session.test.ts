import { afterEach, describe, expect, it, vi } from "vitest";
import { signIn } from "./session";

// Sign-in posts to the host's Development-only endpoint. Outside Development the endpoint is not mapped, and the
// host's answer has no JSON body to read.

afterEach(() => vi.restoreAllMocks());

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
