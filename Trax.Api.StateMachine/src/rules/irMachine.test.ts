import fs from "node:fs";
import { describe, expect, it } from "vitest";
import { SnapshotMachine } from "../machine";
import { differentialFile, irFile } from "../fixtures";
import type { Snapshot } from "../types";
import { machineFromIr, type IrDocument } from "./irMachine";

// The end-to-end cross-language, cross-representation proof: a TS machine built entirely from the IR (the
// artifact the C# source exports) reproduces the committed differential corpus, which was produced from the
// hand-written TS twin (and which the C# engine also matches). So IR-driven TS == hand-written TS == C#.

interface DifferentialCase {
  given: Snapshot;
  when: { trigger: string; input?: unknown };
  expect:
    | { outcome: "transitioned"; wire: string }
    | { outcome: "rejected"; reason: string };
}

describe("turnstile machine built from the IR", () => {
  const ir = JSON.parse(
    fs.readFileSync(irFile("turnstile"), "utf8"),
  ) as IrDocument;
  const machine = new SnapshotMachine(machineFromIr(ir));

  it("starts in the initial state with the schema-derived context", () => {
    const initial = machine.createInitialSnapshot();
    expect(initial.state).toBe("Locked");
    expect(initial.context).toEqual({});
  });

  it("reproduces the differential corpus (IR-driven == hand-written == C#)", () => {
    const corpus = JSON.parse(
      fs.readFileSync(differentialFile("turnstile"), "utf8"),
    ) as {
      cases: DifferentialCase[];
    };

    for (const c of corpus.cases) {
      const label = JSON.stringify(c.when);
      const result = machine.advance(c.given, c.when.trigger, c.when.input);

      if (c.expect.outcome === "transitioned") {
        expect(result.outcome, label).toBe("transitioned");
        if (result.outcome === "transitioned")
          expect(machine.serialize(result.snapshot), label).toBe(c.expect.wire);
      } else {
        expect(result.outcome, label).toBe("rejected");
        if (result.outcome === "rejected")
          expect(result.reason, label).toBe(c.expect.reason);
      }
    }
  });
});
