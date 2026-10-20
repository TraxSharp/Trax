import fs from "node:fs";
import { describe, expect, it } from "vitest";
import { conformanceFile } from "./fixtures";
import { SnapshotMachine } from "./machine";
import { machineFromIr, type IrDocument } from "./rules/irMachine";

// conformance/strict-input/ is written by the C# StrictInputGoldenTests: each value of one field of a strict
// input, and the verdict its CLR type gives it. This engine must give the same one. The table is data, so an
// engine in any other language replays the same file.

type Case = {
  name: string;
  field?: string;
  value?: unknown;
  expect: { outcome: "transitioned"; wire: string } | { outcome: "rejected"; reason: string };
};

const golden = JSON.parse(
  fs.readFileSync(conformanceFile("strict-input", "cases.json"), "utf8"),
) as { start: string; trigger: string; validInput: Record<string, unknown>; cases: Case[] };
const ir = JSON.parse(
  fs.readFileSync(conformanceFile("strict-input", "strict.ir.json"), "utf8"),
) as IrDocument;
const machine = new SnapshotMachine(machineFromIr(ir));

describe("strict input conformance", () => {
  it("has cases", () => {
    expect(golden.cases.length).toBeGreaterThan(100);
  });

  it.each(golden.cases.map((c) => [c.name, c] as const))("%s", (_, c) => {
    const start = machine.rehydrate(golden.start);
    if (start.result !== "ok") throw new Error(`start does not rehydrate: ${start.message}`);
    const input: Record<string, unknown> = structuredClone(golden.validInput);
    if (c.field !== undefined) input[c.field] = c.value;

    const result = machine.advance(start.snapshot, golden.trigger, input);

    expect(
      result.outcome === "transitioned"
        ? { outcome: "transitioned", wire: machine.serialize(result.snapshot) }
        : { outcome: "rejected", reason: result.reason },
    ).toEqual(c.expect);
  });
});
