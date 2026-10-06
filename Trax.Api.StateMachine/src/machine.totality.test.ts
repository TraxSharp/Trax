import { describe, expect, it } from "vitest";
import { SnapshotMachine } from "./machine";
import type { MachineDefinition } from "./types";

// Guards are hand-written per runtime; a throwing guard must degrade to internal-error rather than
// escape advance, exactly like a throwing reducer (PD4 totality applies to guards too). The built-in
// turnstile/checkout guards are throw-proof, so this uses an ad-hoc faulty machine to exercise the path.
describe("engine totality: a throwing guard", () => {
  const def: MachineDefinition<"A" | "B", "Trap"> = {
    id: "faulty",
    version: 1,
    initialState: "A",
    createInitialContext: () => ({}),
    states: ["A", "B"],
    transitions: [
      {
        from: "A",
        trigger: "Trap",
        to: "B",
        guard: () => {
          throw new Error("guard blew up");
        },
      },
    ],
  };
  const machine = new SnapshotMachine(def);

  it("degrades to internal-error, never throws", () => {
    const snapshot = { machine: "faulty", version: 1, state: "A", context: {} };
    expect(() => machine.advance(snapshot, "Trap")).not.toThrow();
    const result = machine.advance(snapshot, "Trap");
    expect(result.outcome).toBe("rejected");
    if (result.outcome === "rejected")
      expect(result.reason).toBe("internal-error");
  });
});
