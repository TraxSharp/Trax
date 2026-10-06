import {
  buildClientSchema,
  getIntrospectionQuery,
  printSchema,
  type IntrospectionQuery,
} from "graphql";
import { beforeAll, describe, expect, test } from "vitest";
import { E2E_KEY, E2E_URL, reachable } from "./helpers";
import { SCHEMA_SDL } from "../mock/schema-sdl";

const STANDARD_INTROSPECTION = new Set([
  "__Schema",
  "__Type",
  "__TypeKind",
  "__Field",
  "__InputValue",
  "__EnumValue",
  "__Directive",
  "__DirectiveLocation",
]);

beforeAll(async () => {
  if (!(await reachable()))
    throw new Error(`e2e devhost unreachable at ${E2E_URL}; run \`npm run test:e2e\``);
});

describe("schema contract", () => {
  // The committed SDL drives the mock's auto-mock schema. If the real API's schema has drifted
  // from it, the mock is stale and this fails, pointing you at `npm run mock:schema`.
  test("committed mock SDL matches the live schema (no drift)", async () => {
    const res = await fetch(E2E_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Api-Key": E2E_KEY },
      body: JSON.stringify({ query: getIntrospectionQuery() }),
    });
    const json = (await res.json()) as { data: IntrospectionQuery };
    // Drop HotChocolate's non-standard "__" types, as scripts/export-schema.mjs does.
    const types = json.data.__schema.types.filter(
      (t) => !t.name.startsWith("__") || STANDARD_INTROSPECTION.has(t.name),
    );
    const live = printSchema(
      buildClientSchema({ __schema: { ...json.data.__schema, types } }),
    );
    const norm = (s: string) => s.replace(/\r\n/g, "\n").trim();
    expect(
      norm(live),
      "live schema differs from src/mock/schema-sdl.ts — run `npm run mock:schema`",
    ).toBe(norm(SCHEMA_SDL));
  });
});
