import { describe, expect, it, vi } from "vitest";
import {
  persistedBody,
  persistedFetch,
  persistedMessageReplacer,
  persistedSubscription,
} from "./persistedDocuments";
import { readRuntimeConfig } from "./runtimeConfig";

const ids = { Overview: "trax_console_Overview_abc" };

const body = (operationName: string) =>
  JSON.stringify({ operationName, query: "query X { y }", variables: { a: 1 }, extensions: {} });

describe("persisted documents", () => {
  it("sends an operation the host registered by its id, without its text", () => {
    expect(JSON.parse(persistedBody(body("Overview"), ids))).toEqual({
      id: "trax_console_Overview_abc",
      operationName: "Overview",
      variables: { a: 1 },
      extensions: {},
    });
  });

  it("sends any other operation as text", () => {
    expect(persistedBody(body("PersistedOperations"), ids)).toBe(body("PersistedOperations"));
  });

  it("leaves urql's fetch alone when the host registered nothing", () => {
    expect(persistedFetch({})).toBeUndefined();
  });

  it("rewrites the body fetch sends", async () => {
    const inner = vi.fn<typeof fetch>(async () => new Response("{}"));
    await persistedFetch(ids, inner)!("/trax/graphql", { method: "POST", body: body("Overview") });
    expect(JSON.parse(inner.mock.calls[0][1]!.body as string).id).toBe("trax_console_Overview_abc");
  });

  it("subscribes by id with an empty query the replacer drops", () => {
    const payload = persistedSubscription(
      { query: "subscription Overview { x }", operationName: "Overview" },
      ids,
    );
    expect(JSON.parse(JSON.stringify(payload, persistedMessageReplacer))).toEqual({
      operationName: "Overview",
      id: "trax_console_Overview_abc",
    });
  });
});

describe("runtime config documents", () => {
  const page = (config: unknown) => {
    const doc = document.implementation.createHTMLDocument();
    const meta = doc.createElement("meta");
    meta.name = "trax-dashboard-config";
    meta.content = JSON.stringify(config);
    doc.head.append(meta);
    return doc;
  };

  it("keeps only ids under the console prefix", () => {
    const config = readRuntimeConfig(
      page({
        documents: {
          Overview: "trax_console_Overview_abc",
          Elsewhere: "trax_client_Elsewhere_abc",
          "not a name": "trax_console_x",
        },
      }),
    );
    expect(config.documents).toEqual({ Overview: "trax_console_Overview_abc" });
  });

  it("is empty when the host names none", () => {
    expect(readRuntimeConfig(page({})).documents).toEqual({});
  });
});
