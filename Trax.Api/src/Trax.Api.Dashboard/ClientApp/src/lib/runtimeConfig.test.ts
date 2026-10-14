import { afterEach, describe, expect, test } from "vitest";
import { graphqlUrls, readRuntimeConfig } from "./runtimeConfig";

function setConfigMeta(content: string | null) {
  document.head.querySelector('meta[name="trax-dashboard-config"]')?.remove();
  if (content === null) return;
  const meta = document.createElement("meta");
  meta.name = "trax-dashboard-config";
  meta.content = content;
  document.head.appendChild(meta);
}

afterEach(() => setConfigMeta(null));

describe("the runtime config the host writes into index.html", () => {
  test("without it (vite dev, the tests), the app is at the root and GraphQL at /trax/graphql", () => {
    expect(readRuntimeConfig()).toEqual({ basePath: "/", graphqlPath: "/trax/graphql" });
  });

  test("reads the base path and the GraphQL path the host serves", () => {
    setConfigMeta(JSON.stringify({ basePath: "/ops/dashboard/", graphqlPath: "/ops/graphql" }));
    expect(readRuntimeConfig()).toEqual({ basePath: "/ops/dashboard/", graphqlPath: "/ops/graphql" });
  });

  test("a field an older host wrote, such as principal, is ignored", () => {
    setConfigMeta(JSON.stringify({ basePath: "/", graphqlPath: "/trax/graphql", principal: "ab12" }));
    expect(readRuntimeConfig()).toEqual({ basePath: "/", graphqlPath: "/trax/graphql" });
  });

  test("ignores anything that is not a same-origin path, so the app never talks to another host", () => {
    setConfigMeta(
      JSON.stringify({ basePath: "https://evil.example/", graphqlPath: "//evil.example/graphql" }),
    );
    expect(readRuntimeConfig()).toEqual({ basePath: "/", graphqlPath: "/trax/graphql" });
  });

  test("ignores a config that is not JSON", () => {
    setConfigMeta("{not json");
    expect(readRuntimeConfig()).toEqual({ basePath: "/", graphqlPath: "/trax/graphql" });
  });
});

describe("the GraphQL URLs", () => {
  test("HTTP is the path; the socket is the same path on this page's host, wss under https", () => {
    expect(graphqlUrls("/ops/graphql", { protocol: "https:", host: "admin.example:8443" })).toEqual({
      http: "/ops/graphql",
      ws: "wss://admin.example:8443/ops/graphql",
    });
    expect(graphqlUrls("/trax/graphql", { protocol: "http:", host: "localhost:5173" })).toEqual({
      http: "/trax/graphql",
      ws: "ws://localhost:5173/trax/graphql",
    });
  });
});
