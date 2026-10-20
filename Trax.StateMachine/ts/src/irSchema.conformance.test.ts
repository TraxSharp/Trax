import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
// @ts-expect-error: a plain node module with no types
import { irValidator } from "../tools/conformance/ir-schema.mjs";
import { conformanceFile, irFile } from "./fixtures";

// conformance/ir.schema.json is the IR's normative shape. Every committed IR validates against it, and every
// object in it is closed, so an IR with a field the schema does not name is refused rather than half-read.

const check = irValidator() as (ir: unknown) => string[];
const machines = fs
  .readdirSync(path.dirname(path.dirname(irFile("turnstile"))), { withFileTypes: true })
  .filter((d) => d.isDirectory() && fs.existsSync(irFile(d.name)))
  .map((d) => d.name);
const committed: [string, string][] = [
  ...machines.map((m): [string, string] => [m, irFile(m)]),
  ["strict-input", conformanceFile("strict-input", "strict.ir.json")],
];
const read = (file: string) => JSON.parse(fs.readFileSync(file, "utf8")) as Record<string, unknown>;

describe("the IR schema", () => {
  it("covers every committed machine", () => {
    expect(machines).toEqual(expect.arrayContaining(["checkout", "gauge", "ingest", "reminder", "turnstile"]));
  });

  it.each(committed)("accepts the committed %s IR", (_, file) => {
    expect(check(read(file))).toEqual([]);
  });

  it("accepts the irFormat a generated twin adds", () => {
    expect(check({ irFormat: 1, ...read(irFile("turnstile")) })).toEqual([]);
  });

  it("refuses a top-level field it does not name", () => {
    expect(check({ ...read(irFile("turnstile")), extra: true })).not.toEqual([]);
  });

  it("refuses a misspelt member of a rule", () => {
    const ir = read(irFile("turnstile")) as { transitions: { guard?: Record<string, unknown> }[] };
    const guarded = ir.transitions.find((t) => t.guard)!;
    guarded.guard = { ...guarded.guard, feild: guarded.guard!.field };
    delete guarded.guard.field;
    expect(check(ir)).not.toEqual([]);
  });

  it("refuses a rule kind it does not name", () => {
    const ir = read(irFile("turnstile")) as { transitions: { guard?: unknown }[] };
    ir.transitions.find((t) => t.guard)!.guard = { rule: "matches", source: "input", field: "coin" };
    expect(check(ir)).not.toEqual([]);
  });

  it("refuses an output on an outcome other than done", () => {
    const ir = read(irFile("ingest")) as { outcomes: Record<string, Record<string, unknown>> };
    ir.outcomes["Fetching.failed"]!.output = { fields: [] };
    expect(check(ir)).not.toEqual([]);
  });
});
