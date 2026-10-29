import fs from "node:fs";
import { describe, expect, it } from "vitest";
import { conformanceFile } from "./fixtures";
import { SnapshotMachine } from "./machine";
import type { PlanRequest, PlanResult } from "./plan";
import { machineFromIr, type IrDocument } from "./rules/irMachine";
import { typedMachineFromIr } from "./typed";
import type { Snapshot } from "./types";

// conformance/plan/ holds the planner's cases over the wizard machine; every engine answers them the same. The cases
// below them are this engine's own: the limits a case file cannot reach cheaply, and the typed view.

type Case = {
  name: string;
  given: Snapshot;
  target: string;
  fields: PlanRequest["fields"];
  navigation: string[];
  maxDepth?: number;
  expect: PlanResult;
};

const golden = JSON.parse(fs.readFileSync(conformanceFile("plan", "cases.json"), "utf8")) as { cases: Case[] };
const wizardIr = JSON.parse(fs.readFileSync(conformanceFile("plan", "wizard.ir.json"), "utf8")) as IrDocument;
const wizard = new SnapshotMachine(machineFromIr(wizardIr));

describe("plan conformance", () => {
  it.each(golden.cases.map((c) => [c.name, c] as const))("%s", (_, c) => {
    expect(
      wizard.plan(c.given, {
        target: c.target,
        fields: c.fields,
        navigation: c.navigation,
        ...(c.maxDepth === undefined ? {} : { maxDepth: c.maxDepth }),
      }),
    ).toEqual(c.expect);
  });
});

type Fields = { email: string | null; name: string | null };
const empty: Snapshot = { machine: "wizard", version: 1, state: "Start", context: { email: null, name: null } };
const filled = { trigger: "Update", input: { email: "ada@example.com", name: "Ada" } };

describe("plan", () => {
  it("does not expand the start when the depth is zero", () => {
    expect(wizard.plan(empty, { target: "Review", fields: filled, navigation: ["Begin", "Next"], maxDepth: 0 })).toEqual({
      result: "exceeded",
    });
  });

  it("answers the start when it is the goal, whatever the depth", () => {
    const review = { ...empty, state: "Review", context: filled.input };
    expect(wizard.plan(review, { target: "Review", fields: filled, navigation: [], maxDepth: 0 })).toEqual({
      result: "plan",
      events: [],
    });
  });

  it("answers in the caller's order, not the machine's", () => {
    // Back and Next both leave Review; the first one named is the one tried first.
    const review = { ...empty, state: "Review", context: filled.input };
    expect(wizard.plan(review, { target: "Details", fields: null, navigation: ["Back"] })).toEqual({
      result: "plan",
      events: [{ trigger: "Back" }],
    });
  });

  it("keeps the path to the custom edge it meets first", () => {
    expect(wizard.plan(empty, { target: "Review", fields: filled, navigation: ["Begin", "Skip"] })).toEqual({
      result: "undetermined",
      trigger: "Skip",
      events: [{ trigger: "Begin" }],
    });
  });

  it("plans through a custom rule its handler is bound for", () => {
    const approving = new SnapshotMachine(machineFromIr(wizardIr, { approved: () => true }));
    const details = { ...empty, state: "Details" };
    expect(approving.plan(details, { target: "Review", fields: null, navigation: ["Skip"] })).toEqual({
      result: "plan",
      events: [{ trigger: "Skip" }],
    });
  });

  // A counter that counts up to `ceiling`: every Up is a new wire, and Never is never reached.
  const counter = (ceiling: number) =>
    new SnapshotMachine(
      machineFromIr(
        {
          id: "counter",
          version: 1,
          initialState: "Counting",
          initialContext: { n: 0 },
          states: ["Counting", "Never"],
          triggers: ["Up"],
          committedStates: [],
          context: {
            Counting: { fields: [{ name: "n", type: "integer", nullable: false, constraints: [] }] },
            Never: { fields: [{ name: "n", type: "integer", nullable: false, constraints: [] }] },
          },
          inputs: {},
          transitions: [
            {
              from: "Counting",
              trigger: "Up",
              to: "Counting",
              guard: { rule: "compare", source: "context", field: "n", op: "lt", value: ceiling },
              reduce: { reduce: "custom", name: "up" },
            },
          ],
        } as IrDocument,
        {},
        { up: (context) => ({ n: (context.n as number) + 1 }) },
      ),
    );
  const count = (ceiling: number) => {
    const machine = counter(ceiling);
    return machine.plan(machine.createInitialSnapshot(), { target: "Never", navigation: ["Up"], maxDepth: 1_000_000 });
  };

  it("stops with a snapshot still waiting after ten thousand", () => {
    expect(count(10_000)).toEqual({ result: "exceeded" });
  });

  it("is unreachable when it runs out of snapshots at exactly ten thousand", () => {
    expect(count(9_999)).toEqual({ result: "unreachable" });
  });

  it("answers a kept custom edge over a limit it reached", () => {
    const details = { ...empty, state: "Details" };
    expect(wizard.plan(details, { target: "Review", fields: filled, navigation: ["Skip"], maxDepth: 1 })).toEqual({
      result: "undetermined",
      trigger: "Skip",
      events: [],
    });
  });

  it("compares snapshots without their null members", () => {
    // Clear writes a null the host would leave out: from the host's snapshot it changes nothing, so it is the goal.
    const note = { fields: [{ name: "note", type: "string", nullable: true, constraints: [] }] };
    const machine = new SnapshotMachine(
      machineFromIr(
        {
          id: "notes",
          version: 1,
          initialState: "Open",
          initialContext: {},
          states: ["Open"],
          triggers: ["Clear"],
          committedStates: [],
          context: { Open: note },
          inputs: {},
          transitions: [{ from: "Open", trigger: "Clear", to: "Open", reduce: { reduce: "custom", name: "clear" } }],
        } as IrDocument,
        {},
        { clear: () => ({ note: null }) },
      ),
    );
    const confirmed = { machine: "notes", version: 1, state: "Open", context: {} };
    expect(machine.plan(confirmed, { target: "Open", fields: { trigger: "Clear" }, navigation: [] })).toEqual({
      result: "plan",
      events: [],
    });
  });

  it("never reaches a goal whose fields event rests on a custom rule", () => {
    const guarded = structuredClone(wizardIr) as IrDocument;
    const update = guarded.transitions.find((t) => t.from === "Review" && t.trigger === "Update")!;
    update.guard = { rule: "custom", name: "editable" };
    const machine = new SnapshotMachine(machineFromIr(guarded));
    const details = { ...empty, state: "Details", context: filled.input };
    expect(machine.plan(details, { target: "Review", fields: filled, navigation: ["Next", "Back"] })).toEqual({
      result: "undetermined",
      trigger: "Update",
      events: [{ trigger: "Next" }],
    });
  });

  it("is on the typed view too", () => {
    const typed = typedMachineFromIr<{
      states: { Start: Fields; Details: Fields; Review: Fields; Sent: Fields };
      triggers: { Begin: undefined; Next: undefined; Back: undefined; Skip: undefined; Send: undefined; Update: Fields };
    }>(wizardIr);
    expect(typed.plan(typed.initial(), { target: "Details", navigation: ["Begin"] })).toEqual({
      result: "plan",
      events: [{ trigger: "Begin" }],
    });
  });
});
