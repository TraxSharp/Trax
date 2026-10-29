import { afterEach, describe, expect, test, vi } from "vitest";
import {
  httpTransport,
  persistedOperationsAvailableDocument,
  serverVersionDocument,
  TraxTransportError,
} from "../trax/index.g";
import { client } from "./graphql";

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const answer = (data: unknown) =>
  new Response(JSON.stringify({ data }), { headers: { "Content-Type": "application/json" } });

// What each request's body was.
function bodies(fetchSpy: { mock: { calls: unknown[][] } }) {
  return fetchSpy.mock.calls.map(([, init]) => JSON.parse((init as RequestInit).body as string));
}

describe("the console's client", () => {
  // Trax refuses GraphQL over GET unless the host opts in, so a query sent as GET fails against
  // every real host.
  test("sends queries as POST", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ operations: null }));

    await client.query(serverVersionDocument, {});

    expect(fetchSpy).toHaveBeenCalled();
    expect((fetchSpy.mock.calls[0][1] as RequestInit).method).toBe("POST");
  });

  // The test page names no document ids, as a host without persisted operations does.
  test("sends every document as text when the host named no ids", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ operations: null }));

    await client.query(serverVersionDocument, {});

    expect(bodies(fetchSpy)[0]).toEqual({ query: serverVersionDocument.text, operationName: "ServerVersion", variables: {} });
  });
});

describe("the generated transport", () => {
  const transport = (byId?: Parameters<typeof httpTransport>[0]["byId"]) =>
    httpTransport({ url: "/trax/graphql", socketUrl: "ws://localhost/trax/graphql", byId });

  test("sends a document the host registered by its id, without its text", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ operations: null }));

    await transport().execute({ document: serverVersionDocument, variables: { a: 1 } });

    expect(bodies(fetchSpy)[0]).toEqual({ id: serverVersionDocument.id, operationName: "ServerVersion", variables: { a: 1 } });
  });

  test("sends a document the host did not register as text", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ operations: null }));

    await transport(() => false).execute({ document: serverVersionDocument, variables: {} });

    expect(bodies(fetchSpy)[0].query).toBe(serverVersionDocument.text);
    expect(bodies(fetchSpy)[0].id).toBeUndefined();
  });

  test("sends persisted-operation management as text, since no host registers it", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ persistedOperations: null }));

    await transport().execute({ document: persistedOperationsAvailableDocument, variables: {} });

    expect(persistedOperationsAvailableDocument.id).toBeNull();
    expect(bodies(fetchSpy)[0].query).toBe(persistedOperationsAvailableDocument.text);
  });

  test("a body that is not a GraphQL answer is a transport error naming the status", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("<html>", { status: 502 }));

    await expect(transport().execute({ document: serverVersionDocument, variables: {} })).rejects.toThrow(
      "The host answered 502 with no GraphQL answer.",
    );
  });
});

describe("the generated transport's checks", () => {
  const options = { url: "/trax/graphql", socketUrl: "wss://ops.example.com/trax/graphql" };

  test.each(["/trax/graphql", "https://ops.example.com/trax/graphql", "http://localhost:5310/trax/graphql", "http://127.0.0.1/g"])(
    "accepts the url %s",
    (url) => expect(() => httpTransport({ ...options, url })).not.toThrow(),
  );

  test("refuses a cleartext url to another host", () =>
    expect(() => httpTransport({ ...options, url: "http://ops.example.com/trax/graphql" })).toThrow("is not over TLS"));

  test.each(["wss://ops.example.com/trax/graphql", "ws://localhost:5310/trax/graphql", "ws://127.0.0.1/g", "ws://[::1]/g"])(
    "accepts the socket %s",
    (socketUrl) => expect(() => httpTransport({ ...options, socketUrl })).not.toThrow(),
  );

  test("refuses a cleartext socket to another host", () =>
    expect(() => httpTransport({ ...options, socketUrl: "ws://ops.example.com/trax/graphql" })).toThrow("is not over TLS"));

  test.each(["/trax/graphql", "https://ops.example.com/trax/graphql"])("refuses the socket %s, which is not ws or wss", (socketUrl) =>
    expect(() => httpTransport({ ...options, socketUrl })).toThrow(/not (an absolute )?(a )?ws:\/\/ or wss:\/\/ URL/),
  );

  test("accepts a cleartext socket to the origin of a page served over http", () => {
    vi.stubGlobal("location", { protocol: "http:", host: "ops.lan:8080" });

    expect(() => httpTransport({ ...options, socketUrl: "ws://ops.lan:8080/trax/graphql" })).not.toThrow();
    expect(() => httpTransport({ ...options, socketUrl: "ws://other.lan:8080/trax/graphql" })).toThrow("is not over TLS");
  });

  test("allowInsecureTransport lifts both refusals", () =>
    expect(() =>
      httpTransport({
        url: "http://ops.lan/trax/graphql",
        socketUrl: "ws://ops.lan/trax/graphql",
        allowInsecureTransport: true,
      }),
    ).not.toThrow());

  test("follows no redirect", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ operations: null }));

    await httpTransport(options).execute({ document: serverVersionDocument, variables: {} });

    expect((fetchSpy.mock.calls[0][1] as RequestInit).redirect).toBe("error");
  });

  // redirect: "error" is a request, not a promise: a polyfill or a stand-in fetch may follow anyway.
  test("an answer a fetch followed a redirect to is a transport error, not read", async () => {
    const followed = answer({ operations: { serverVersion: "elsewhere" } });
    Object.defineProperty(followed, "redirected", { value: true });
    vi.spyOn(globalThis, "fetch").mockResolvedValue(followed);

    const sent = httpTransport(options).execute({ document: serverVersionDocument, variables: {} });

    await expect(sent).rejects.toBeInstanceOf(TraxTransportError);
    await expect(sent).rejects.toThrow("redirect");
  });

  test("a redirect handed back as it is, is a transport error", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 302, headers: { Location: "https://x" } }));

    await expect(httpTransport(options).execute({ document: serverVersionDocument, variables: {} })).rejects.toThrow(
      "redirect (302)",
    );
  });

  test("an answer that is not 2xx and names no GraphQL error is a transport error, whatever data it holds", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ data: { operations: null } }), { status: 500 }),
    );

    const sent = httpTransport(options).execute({ document: serverVersionDocument, variables: {} });

    await expect(sent).rejects.toBeInstanceOf(TraxTransportError);
    await expect(sent).rejects.toThrow("The host answered 500 with no GraphQL error.");
  });

  test("an answer that is not 2xx with a GraphQL error is the host's answer", async () => {
    const errors = [{ message: "Not authorized.", extensions: { code: "AUTH_NOT_AUTHORIZED" } }];
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify({ errors }), { status: 401 }));

    await expect(httpTransport(options).execute({ document: serverVersionDocument, variables: {} })).resolves.toEqual({
      errors,
    });
  });

  test("an answer longer than maxResponseBytes is a transport error, read no further", async () => {
    const body = JSON.stringify({ data: { operations: { serverVersion: "x".repeat(2048) } } });
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(body, { status: 200 }));

    await expect(
      httpTransport({ ...options, maxResponseBytes: 1024 }).execute({ document: serverVersionDocument, variables: {} }),
    ).rejects.toThrow("more than 1024 bytes");
  });

  test("an answer whose Content-Length is over the limit is refused before it is read", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("{}", { status: 200, headers: { "Content-Length": String(32 * 1024 * 1024) } }),
    );

    await expect(httpTransport(options).execute({ document: serverVersionDocument, variables: {} })).rejects.toThrow(
      `more than ${16 * 1024 * 1024} bytes`,
    );
  });

  test("an answer within the limit is read", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ operations: null }));

    await expect(
      httpTransport({ ...options, maxResponseBytes: 64 }).execute({ document: serverVersionDocument, variables: {} }),
    ).resolves.toEqual({ data: { operations: null } });
  });
});
