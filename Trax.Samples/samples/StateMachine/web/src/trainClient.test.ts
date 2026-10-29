import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { configureTrax, stateMachineListMachines, TraxOperationError } from "./trax/index.g";

// A train function is the generated sendDocument: it posts the document's id and reads the field it asked for. These
// pin how it answers a fetch that did not get the endpoint's own 2xx answer.

const machines = { discover: { stateMachine: { listMachines: [] } } };
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

beforeEach(() => configureTrax({ url: "/trax/graphql", socketUrl: "wss://example.test/trax/graphql" }));

afterEach(() => vi.restoreAllMocks());

describe("a train function", () => {
  it("reads the field it asked for", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(json({ data: machines }));

    await expect(stateMachineListMachines()).resolves.toEqual([]);
  });

  // redirect: "error" is a request, not a promise: a polyfill or a stand-in fetch may follow anyway.
  it("does not read an answer a fetch followed a redirect to", async () => {
    const followed = json({ data: machines });
    Object.defineProperty(followed, "redirected", { value: true });
    vi.spyOn(globalThis, "fetch").mockResolvedValue(followed);

    await expect(stateMachineListMachines()).rejects.toThrow("redirect");
  });

  it("refuses a redirect handed back as it is", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 302, headers: { Location: "https://x" } }));

    await expect(stateMachineListMachines()).rejects.toThrow("redirect (302)");
  });

  it("fails on an answer that is not 2xx and names no GraphQL error, rather than reading no data", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(json({}, 500));

    const sent = stateMachineListMachines();

    await expect(sent).rejects.toBeInstanceOf(TraxOperationError);
    await expect(sent).rejects.toThrow("The host answered 500.");
  });

  it("fails on an answer that is not 2xx and not JSON, naming the status", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("<html>", { status: 502 }));

    await expect(stateMachineListMachines()).rejects.toThrow("The host answered 502.");
  });

  it.each([{}, null, 5, { data: null }])("fails on a 2xx answer with no data to read (%j), rather than reading undefined", async (body) => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(json(body));

    const sent = stateMachineListMachines();

    await expect(sent).rejects.toBeInstanceOf(TraxOperationError);
    await expect(sent).rejects.toThrow("The host answered 200 with no GraphQL answer.");
  });

  it("throws the host's GraphQL error, whatever the status", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      json({ errors: [{ message: "Not authorized.", extensions: { code: "AUTH_NOT_AUTHORIZED" } }] }, 401),
    );

    await expect(stateMachineListMachines()).rejects.toMatchObject({ code: "AUTH_NOT_AUTHORIZED" });
  });
});
