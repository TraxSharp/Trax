import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { conformanceFile } from "./fixtures";
import { RefusalCode } from "./results";

interface Entry {
  name: string;
  code: string;
  scope: string;
  uncovered?: string;
}

const registry = (): Entry[] =>
  (JSON.parse(fs.readFileSync(conformanceFile("refusal-codes.json"), "utf8")) as { codes: Entry[] }).codes;

function jsonFilesUnder(dir: string): string[] {
  return fs
    .readdirSync(dir, { withFileTypes: true, recursive: true })
    .filter((e) => e.isFile() && e.name.endsWith(".json"))
    .map((e) => path.join(e.parentPath, e.name));
}

// Every string a `reason` or `code` member holds, at any depth: where a case writes the refusal it expects.
function codesIn(files: string[]): Set<string> {
  const found = new Set<string>();
  const walk = (value: unknown): void => {
    if (Array.isArray(value)) value.forEach(walk);
    else if (value !== null && typeof value === "object")
      for (const [key, member] of Object.entries(value)) {
        if ((key === "reason" || key === "code" || key === "error") && typeof member === "string") found.add(member);
        walk(member);
      }
  };
  for (const file of files) walk(JSON.parse(fs.readFileSync(file, "utf8")));
  return found;
}

// conformance/refusal-codes.json is the one closed set of refusal codes; the C# RefusalCodeParityTests holds the
// C# enum (and so the GraphQL enum) equal to it, and this holds the package's RefusalCode equal to it.
describe("refusal codes", () => {
  it("are the shared registry's, in its order", () => {
    expect(Object.entries(RefusalCode).map(([name, code]) => ({ name, code }))).toEqual(
      registry().map(({ name, code }) => ({ name, code })),
    );
  });

  // The C# RefusalCodeParityTests checks the same.
  it("are each held by a kit case of their scope, or say why not", () => {
    const engine = codesIn(
      ["machines", "strict-input", "plan"].flatMap((d) => jsonFilesUnder(conformanceFile(d))),
    );
    const client = codesIn(jsonFilesUnder(conformanceFile("client", "transcripts")));
    const wrong = registry().flatMap((e) => {
      if (!["engine", "client", "host"].includes(e.scope)) return [`${e.code}: scope '${e.scope}'`];
      if (e.scope === "host") return e.uncovered === undefined ? [] : [`${e.code}: a host code needs no 'uncovered'`];
      const held = (e.scope === "engine" ? engine : client).has(e.code);
      if (held && e.uncovered !== undefined) return [`${e.code}: a case holds it, so 'uncovered' is stale`];
      if (!held && e.uncovered === undefined) return [`${e.code}: no ${e.scope} case holds it`];
      return [];
    });
    expect(wrong).toEqual([]);
  });
});
