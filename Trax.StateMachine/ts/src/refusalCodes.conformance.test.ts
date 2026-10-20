import fs from "node:fs";
import { describe, expect, it } from "vitest";
import { conformanceFile } from "./fixtures";
import { RefusalCode } from "./results";

// conformance/refusal-codes.json is the one closed set of refusal codes; the C# RefusalCodeParityTests holds the
// C# enum (and so the GraphQL enum) equal to it, and this holds the package's RefusalCode equal to it.
describe("refusal codes", () => {
  it("are the shared registry's, in its order", () => {
    const registry = (
      JSON.parse(fs.readFileSync(conformanceFile("refusal-codes.json"), "utf8")) as {
        codes: { name: string; code: string }[];
      }
    ).codes;

    expect(Object.entries(RefusalCode).map(([name, code]) => ({ name, code }))).toEqual(registry);
  });
});
