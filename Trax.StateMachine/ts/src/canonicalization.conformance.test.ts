import fs from "node:fs";
import { describe, expect, it } from "vitest";
import { conformanceFile } from "./fixtures";
import { turnstileCore } from "./machines/turnstile/turnstile";
import type { Snapshot } from "./types";

// RFC 8785 (JCS) requires numbers and strings to serialize exactly as ECMAScript JSON.stringify. The vectors are
// conformance/canonical-wire.json, which every engine reads; add one there, not here.
const wire = (context: Record<string, unknown>) =>
  turnstileCore.serialize({
    machine: "m",
    version: 1,
    state: "S",
    context,
  } as Snapshot);
const envelope = (ctx: string) =>
  `{"machine":"m","version":1,"state":"S","context":${ctx}}`;

type Vector = { name: string; context?: string; stringUnits?: number[]; keyUnits?: number[]; wire: string };

const vectors = (
  JSON.parse(fs.readFileSync(conformanceFile("canonical-wire.json"), "utf8")) as { cases: Vector[] }
).cases;

const contextOf = (v: Vector): Record<string, unknown> => {
  if (v.context !== undefined) return JSON.parse(v.context) as Record<string, unknown>;
  if (v.stringUnits) return { s: String.fromCharCode(...v.stringUnits) };
  if (v.keyUnits) return { [String.fromCharCode(...v.keyUnits)]: 1 };
  throw new Error(`${v.name}: no context`);
};

describe("canonical wire conformance (RFC 8785 / ECMAScript)", () => {
  it("has vectors", () => {
    expect(vectors.length).toBeGreaterThan(20);
  });

  it.each(vectors.map((v) => [v.name, v] as const))("%s", (_, v) => {
    expect(wire(contextOf(v))).toBe(envelope(v.wire));
  });
});

// A twin reducer can build a value JSON.stringify would write differently from a member-by-member walk; the wire is
// what JSON.stringify writes, in canonical order.
describe("canonical wire of values JSON.stringify treats specially", () => {
  it("writes a hole as null", () => {
    expect(wire({ a: [, 1] })).toBe(envelope('{"a":[null,1]}'));
    expect(wire({ a: new Array(2) })).toBe(envelope('{"a":[null,null]}'));
  });

  it("skips a function member", () => {
    expect(wire({ a: 1, f: () => 1, s: Symbol("s") })).toBe(envelope('{"a":1}'));
    expect(wire({ a: [() => 1] })).toBe(envelope('{"a":[null]}'));
  });

  it("honours toJSON", () => {
    const at = new Date(Date.UTC(2026, 9, 15));
    expect(wire({ at, o: { toJSON: (key: string) => ({ key, z: 1, b: 2 }) } })).toBe(
      envelope('{"at":"2026-10-15T00:00:00.000Z","o":{"b":2,"key":"o","z":1}}'),
    );
  });
});
