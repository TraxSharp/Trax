import { describe, expect, test } from "vitest";
import { buildInputJson, coerce, isBooleanProperty, isEnumProperty } from "./trainInput";
import type { InputPropertySchema } from "../types";

const schema: InputPropertySchema[] = [
  { name: "playerId", typeName: "String", isNullable: false, enumValues: null },
  { name: "amount", typeName: "Int32", isNullable: true, enumValues: null },
  { name: "tier", typeName: "PlayerTier", isNullable: false, enumValues: ["Bronze", "Gold"] },
  { name: "notify", typeName: "Boolean", isNullable: false, enumValues: null },
];

describe("train input", () => {
  test("enum and boolean properties are recognised", () => {
    expect(isEnumProperty(schema[2])).toBe(true);
    expect(isEnumProperty(schema[0])).toBe(false);
    expect(isBooleanProperty(schema[3])).toBe(true);
    expect(isBooleanProperty(schema[1])).toBe(false);
  });

  test("builds JSON by type, passing an enum member through as given", () => {
    const json = buildInputJson(schema, { playerId: "p-1", amount: "5", tier: "Gold", notify: "true" });
    expect(JSON.parse(json)).toEqual({ playerId: "p-1", amount: 5, tier: "Gold", notify: true });
  });

  test("omits empty fields, and nothing filled is no input", () => {
    expect(JSON.parse(buildInputJson(schema, { playerId: "p-1", amount: "" }))).toEqual({ playerId: "p-1" });
    expect(buildInputJson(schema, {})).toBe("");
  });

  test("a non-numeric value in a number field is sent as typed", () => {
    expect(coerce("Int64", "abc")).toBe("abc");
    expect(coerce("Decimal", "1.5")).toBe(1.5);
  });
});
