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

describe("machine built from an IR with per-state invariants", () => {
  const ir: IrDocument = {
    id: "inv",
    version: 1,
    initialState: "Draft",
    states: ["Draft", "Done"],
    triggers: ["Finish"],
    committedStates: [],
    context: {
      Draft: {
        fields: [{ name: "body", type: "string", nullable: false, constraints: [] }],
      },
      Done: {
        fields: [{ name: "body", type: "string", nullable: false, constraints: [] }],
      },
    },
    inputs: {},
    invariants: {
      Done: { rule: "length", source: "context", field: "body", op: "gte", value: 6 },
    },
    transitions: [{ from: "Draft", trigger: "Finish", to: "Done" }],
  };
  const machine = new SnapshotMachine(machineFromIr(ir));
  const wire = (context: unknown) =>
    JSON.stringify({ machine: "inv", version: 1, state: "Done", context });

  it("enforces the schema AND the invariant on rehydrate", () => {
    expect(machine.rehydrate(wire({ body: "123456" })).result).toBe("ok");
    expect(machine.rehydrate(wire({ body: "short" })).result).not.toBe("ok");
    expect(machine.rehydrate(wire({ body: 5 })).result).not.toBe("ok");
  });
});

describe("machine built from an IR with a custom invariant", () => {
  const ir: IrDocument = {
    id: "priced",
    version: 1,
    initialState: "Cart",
    states: ["Cart"],
    triggers: [],
    committedStates: [],
    context: {
      Cart: {
        fields: [
          { name: "total", type: "integer", nullable: false, constraints: [] },
          { name: "note", type: "string", nullable: true, constraints: [] },
        ],
      },
    },
    inputs: {},
    invariants: {
      Cart: {
        rule: "all",
        rules: [
          { rule: "absent", source: "context", field: "note" },
          { rule: "custom", name: "priced:total" },
        ],
      },
    },
    transitions: [],
  };
  const wire = (context: unknown) =>
    JSON.stringify({ machine: "priced", version: 1, state: "Cart", context });
  const fromServer = { fromServer: true };

  it("refuses an unhandled custom rule, except on a snapshot the server sent", () => {
    const machine = new SnapshotMachine(machineFromIr(ir));
    expect(machine.rehydrate(wire({ total: 5, note: null })).result).not.toBe("ok");
    expect(machine.rehydrate(wire({ total: 5, note: null }), fromServer).result).toBe("ok");
  });

  it("still checks the shape and the built-in rules of a snapshot the server sent", () => {
    const machine = new SnapshotMachine(machineFromIr(ir));
    expect(machine.rehydrate(wire({ total: 5, note: "x" }), fromServer).result).not.toBe("ok");
    expect(machine.rehydrate(wire({ total: "5", note: null }), fromServer).result).not.toBe("ok");
  });

  it("evaluates a bound handler, on every read", () => {
    const machine = new SnapshotMachine(
      machineFromIr(ir, { "priced:total": (ctx) => ctx.total === 5 }),
    );
    expect(machine.rehydrate(wire({ total: 5, note: null })).result).toBe("ok");
    expect(machine.rehydrate(wire({ total: 6, note: null })).result).not.toBe("ok");
    expect(machine.rehydrate(wire({ total: 6, note: null }), fromServer).result).not.toBe("ok");
  });
});

describe("machine built from an IR with an effect edge", () => {
  const ir = JSON.parse(
    fs.readFileSync(irFile("checkout"), "utf8"),
  ) as IrDocument;

  it("names no server type on the edge", () => {
    const pay = ir.transitions.find((t) => t.trigger === "Pay");
    expect(pay?.effect).toEqual({ keyPrefix: "checkout:charge" });
  });

  it("treats an effect without a type as effect-bound", () => {
    // Without a declared record, an effect-bound trigger takes the receipt the effect runner hands it, and any
    // other trigger takes nothing. The edge carries only keyPrefix, so that alone must mark it.
    const withoutRecord: IrDocument = { ...ir, inputs: {} };
    const unbound: IrDocument = {
      ...withoutRecord,
      transitions: withoutRecord.transitions.map(({ effect: _, ...t }) => t),
    };
    const review: Snapshot = {
      machine: "checkout",
      version: 1,
      state: "Review",
      context: { currency: "USD", items: ["book"], receipt: null, total: 5 },
    };
    const receipt = { receipt: "rcpt_1" };

    expect(
      new SnapshotMachine(machineFromIr(withoutRecord)).canFire(
        review,
        "Pay",
        receipt,
      ),
    ).toBe(true);
    expect(
      new SnapshotMachine(machineFromIr(unbound)).canFire(
        review,
        "Pay",
        receipt,
      ),
    ).toBe(false);
  });
});
