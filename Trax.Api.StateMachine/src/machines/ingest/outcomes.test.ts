import fs from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { irFile } from "../../fixtures";
import { SnapshotMachine } from "../../machine";
import { generateContextTypes } from "../../rules/generateTypes";
import { machineFromIr, type IrDocument } from "../../rules/irMachine";
import { ingest, ingestCore } from "./ingest";

// A state that invokes a train, seen from the twin: its outcomes are events the twin applies through the same
// declarative guards and reductions as the server. The twin never starts the train.

const ir = JSON.parse(fs.readFileSync(irFile("ingest"), "utf8")) as IrDocument;
const fetching = ingest.snapshotAt("Fetching", { source: "s3://bucket/a" });

describe("ingest outcomes in the twin", () => {
  it("routes an unsure output to its own target through the guarded OnDone", () => {
    const r = ingest.advance(fetching, "Fetching.done", {
      fingerprint: "sha256:def",
      unsure: true,
    });
    expect(r).toEqual({
      outcome: "transitioned",
      snapshot: {
        machine: "ingest",
        version: 1,
        state: "NeedsReview",
        context: { fingerprint: "sha256:def", source: "s3://bucket/a" },
      },
    });
  });

  it("takes the unguarded OnDone after the guarded one for a sure output", () => {
    const r = ingest.advance(fetching, "Fetching.done", {
      fingerprint: "sha256:abc",
      unsure: false,
    });
    expect(r.outcome === "transitioned" && r.snapshot.state).toBe("Fetched");
  });

  it("applies failed and cancelled outcomes with no input", () => {
    const failed = ingest.advance(fetching, "Fetching.failed");
    const cancelled = ingest.advance(fetching, "Fetching.cancelled");
    expect(failed.outcome === "transitioned" && failed.snapshot.state).toBe("FetchFailed");
    expect(cancelled.outcome === "transitioned" && cancelled.snapshot.state).toBe("Cancelled");
  });

  it("is a no-transition when the state was already left", () => {
    const left = ingest.snapshotAt("Idle", { source: "s3://bucket/a" });
    const r = ingest.advance(left, "Fetching.failed");
    expect(r).toMatchObject({ outcome: "rejected", reason: "no-transition" });
  });

  it("is a no-transition, not guard-failed, when no OnDone edge accepts the output", () => {
    const guardedOnly: IrDocument = {
      ...ir,
      outcomes: {
        ...ir.outcomes,
        "Fetching.done": {
          ...ir.outcomes!["Fetching.done"],
          edges: [ir.outcomes!["Fetching.done"].edges[0]],
        },
      },
    };
    const machine = new SnapshotMachine(machineFromIr(guardedOnly));
    const r = machine.advance(fetching, "Fetching.done", {
      fingerprint: "sha256:abc",
      unsure: false,
    });
    expect(r).toMatchObject({ outcome: "rejected", reason: "no-transition" });
  });

  it("never offers an outcome as an action", () => {
    expect(ingestCore.availableTriggers(fetching)).toEqual(["Abandon"]);
    expect(ingestCore.canFire(fetching, "Fetching.failed")).toBe(false);
  });

  it("lists outcome edges in describe(), as the C# engine does", () => {
    const d = ingestCore.describe();
    expect(d.triggers).toContain("Fetching.done");
    expect(d.transitions).toContainEqual({
      from: "Fetching",
      trigger: "Fetching.done",
      to: "NeedsReview",
    });
  });
});

describe("ingest generated twin", () => {
  it("regenerates the committed ingest context types (drift check)", () => {
    expect(generateContextTypes(ir)).toBe(
      fs.readFileSync(
        fileURLToPath(new URL("./ingest.contexts.g.ts", import.meta.url)),
        "utf8",
      ),
    );
  });

  it("types each outcome, with the train's output as the success outcome's input", () => {
    const out = generateContextTypes(ir);
    expect(out).toContain(
      'export type IngestOutcome = "Fetching.cancelled" | "Fetching.done" | "Fetching.failed";',
    );
    expect(out).toContain('"Fetching.done": IngestFetchingDoneOutput;');
    expect(out).toContain('"Fetching.failed": undefined;');
    expect(out).toContain("export type IngestFetchingDoneOutput = {");
  });
});
