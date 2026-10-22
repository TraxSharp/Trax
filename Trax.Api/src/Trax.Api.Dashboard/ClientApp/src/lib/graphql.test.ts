import { afterEach, describe, expect, test, vi } from "vitest";
import { httpTransport, persistedOperationsAvailableDocument, serverVersionDocument } from "../trax/index.g";
import { client } from "./graphql";

afterEach(() => vi.restoreAllMocks());

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
    httpTransport({ url: "/trax/graphql", socketUrl: "ws://host/trax/graphql", byId });

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
