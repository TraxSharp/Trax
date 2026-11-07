import { describe, expect, it } from "vitest";
import { boardGraph, hasProvenance, type Board } from "./machineBoard";

const board: Board = {
  machine: "order",
  lanes: ["order", "shop"],
  stickies: [
    { id: "command:order:Place", kind: "command", machine: "order", label: "Place", scenarios: [{ file: "o.feature", line: 3, scenario: "Ada places her order" }] },
    { id: "event:order:Draft:Place:Placed", kind: "event", machine: "order", label: "Draft → Placed", scenarios: [] },
  ],
  links: [{ from: "command:order:Place", to: "event:order:Draft:Place:Placed" }],
};

describe("boardGraph", () => {
  it("writes each sticky's role and lane under its label, so colour does not carry it alone", () => {
    const { nodes, edges } = boardGraph(board);
    expect(nodes[0]).toMatchObject({ label: "Place", detail: "command · order", tone: "info" });
    expect(nodes[1]).toMatchObject({ detail: "event · order", tone: "warn" });
    expect(edges).toEqual([{ id: "command:order:Place->event:order:Draft:Place:Placed", source: "command:order:Place", target: "event:order:Draft:Place:Placed" }]);
  });

  it("leaves the lane out when there is one", () => {
    expect(boardGraph({ ...board, lanes: ["order"] }).nodes[0].detail).toBe("command");
  });

  it("tells a board drawn without provenance", () => {
    expect(hasProvenance(board)).toBe(true);
    expect(hasProvenance({ ...board, stickies: board.stickies.map((s) => ({ ...s, scenarios: [] })) })).toBe(false);
  });
});
