import { describe, expect, it } from "vitest";
import { turnstileCore } from "./machines/turnstile/turnstile";
import type { Snapshot } from "./types";

// RFC 8785 (JCS) requires numbers and strings to serialize exactly as ECMAScript JSON.stringify. The TS twin
// delegates value emission to JSON.stringify, so it is compliant by construction; this locks that it stays so,
// and the vectors are identical to the C# CanonicalizationConformanceTests, keeping both runtimes pinned to
// the same table.
const wire = (context: Record<string, unknown>) =>
  turnstileCore.serialize({
    machine: "m",
    version: 1,
    state: "S",
    context,
  } as Snapshot);
const envelope = (ctx: string) =>
  `{"machine":"m","version":1,"state":"S","context":${ctx}}`;

describe("canonical wire conformance (RFC 8785 / ECMAScript)", () => {
  it.each<[number, string]>([
    [0.1, "0.1"],
    [1e21, "1e+21"],
    [1e20, "100000000000000000000"],
    [1e-7, "1e-7"],
    [1e-6, "0.000001"],
    [5e-324, "5e-324"],
    [1.7976931348623157e308, "1.7976931348623157e+308"],
    [1.2345678901234568e20, "123456789012345680000"],
    [100, "100"],
    [-1.5, "-1.5"],
    [2.5, "2.5"],
    [1, "1"],
    [-0, "0"],
  ])("formats number %p as %s", (value, expected) => {
    expect(wire({ n: value })).toBe(envelope(`{"n":${expected}}`));
  });

  it("keeps non-ASCII and astral characters literal", () => {
    expect(wire({ s: "café" })).toBe(envelope('{"s":"café"}'));
    expect(wire({ s: "😀" })).toBe(envelope('{"s":"😀"}'));
  });

  it("uses short escapes and lowercase hex for control characters", () => {
    // Input holds U+0001, tab, newline, U+001F. The backslash in the expected is built from its code point
    // so the literal escape text is not written into the source.
    const bs = String.fromCharCode(0x5c);
    const input = String.fromCharCode(0x01, 0x09, 0x0a, 0x1f);
    expect(wire({ s: input })).toBe(
      envelope(`{"s":"${bs}u0001${bs}t${bs}n${bs}u001f"}`),
    );
  });
});
