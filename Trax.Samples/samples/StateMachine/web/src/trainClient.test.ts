import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { checkoutServer, configureTrax, stateMachineListMachines, TraxOperationError } from "./trax/index.g";

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

// What configureTrax is given holds for every train function and every machine server made on it.
describe("the connection's limits", () => {
  const connection = { url: "/trax/graphql", socketUrl: "wss://example.test/trax/graphql" };
  const instance = "00000000-0000-0000-0000-000000000001";
  const large = () => json({ data: machines, padding: "x".repeat(256) });
  const never = () => new Promise<Response>(() => {});

  it("a train function reads no more of an answer than maxResponseBytes", async () => {
    configureTrax({ ...connection, maxResponseBytes: 64 });
    vi.spyOn(globalThis, "fetch").mockImplementation(async () => large());

    await expect(stateMachineListMachines()).rejects.toThrow("more than 64 bytes");
  });

  it("a machine server reads no more of an answer than maxResponseBytes", async () => {
    configureTrax({ ...connection, maxResponseBytes: 64 });
    vi.spyOn(globalThis, "fetch").mockImplementation(async () => large());

    await expect(checkoutServer().get(instance)).rejects.toThrow("larger than 64 bytes");
  });

  it("a train function gives up on a host that does not answer within requestTimeout", async () => {
    configureTrax({ ...connection, requestTimeout: 50 });
    vi.spyOn(globalThis, "fetch").mockImplementation(never);

    const sent = stateMachineListMachines();

    await expect(sent).rejects.toBeInstanceOf(TraxOperationError);
    await expect(sent).rejects.toThrow("did not answer within 50 ms");
  });

  it("a machine server gives up on a host that does not answer within requestTimeout", async () => {
    configureTrax({ ...connection, requestTimeout: 50 });
    vi.spyOn(globalThis, "fetch").mockImplementation(never);

    await expect(checkoutServer().get(instance)).rejects.toThrow("did not answer within 50 ms");
  });

  it.each([Number.NaN, -1, 0])("refuses a requestTimeout of %s", (requestTimeout) => {
    expect(() => configureTrax({ ...connection, requestTimeout })).toThrow(RangeError);
  });

  // The machine servers take the limit as the engine's rule has it, so configureTrax refuses what they would refuse
  // later, while a page renders.
  it.each([0, -1, 1048576.5, Number.NaN, Number.POSITIVE_INFINITY])(
    "refuses a maxResponseBytes of %s",
    (maxResponseBytes) => {
      expect(() => configureTrax({ ...connection, maxResponseBytes })).toThrow(RangeError);
    },
  );
});

// The socket every machine server shares: a host that turns the caller away with 4403 gives a retry the same answer,
// so the close reaches the stream, which stops as FORBIDDEN instead of the page waiting on a socket opened forever.
describe("the shared socket", () => {
  const instance = "00000000-0000-0000-0000-000000000001";

  it("hands a 4403 close to the stream instead of opening the socket again", async () => {
    let opened = 0;
    class RefusingSocket {
      static readonly CONNECTING = 0;
      static readonly OPEN = 1;
      static readonly CLOSING = 2;
      static readonly CLOSED = 3;
      readyState = RefusingSocket.CONNECTING;
      onopen: (() => void) | null = null;
      onmessage: ((event: { data: string }) => void) | null = null;
      onclose: ((event: { code: number; reason: string; wasClean: boolean }) => void) | null = null;
      onerror: ((event: unknown) => void) | null = null;
      constructor() {
        opened += 1;
        setTimeout(() => {
          this.readyState = RefusingSocket.OPEN;
          this.onopen?.();
        });
      }
      send(data: string) {
        const { type } = JSON.parse(data) as { type: string };
        if (type === "connection_init") setTimeout(() => this.onmessage?.({ data: JSON.stringify({ type: "connection_ack" }) }));
        if (type === "subscribe") setTimeout(() => this.close(4403, "Forbidden"));
      }
      close(code = 1000, reason = "") {
        if (this.readyState === RefusingSocket.CLOSED) return;
        this.readyState = RefusingSocket.CLOSED;
        this.onclose?.({ code, reason, wasClean: true });
      }
    }
    vi.stubGlobal("WebSocket", RefusingSocket);
    configureTrax({ url: "/trax/graphql", socketUrl: "wss://example.test/trax/graphql" });
    const messages: unknown[] = [];

    const stop = checkoutServer().subscribe(instance, (message) => messages.push(message));
    try {
      await vi.waitFor(() => expect(messages).toEqual([expect.objectContaining({ type: "ended", code: "FORBIDDEN" })]));
      expect(opened).toBe(1);
    } finally {
      stop();
      vi.unstubAllGlobals();
    }
  });
});
