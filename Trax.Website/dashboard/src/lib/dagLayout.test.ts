import { describe, expect, test } from "vitest";
import { computeDagLayout } from "./dagLayout";

describe("computeDagLayout", () => {
  test("empty graph yields an empty layout", () => {
    const l = computeDagLayout([], []);
    expect(l.nodes).toHaveLength(0);
    expect(l.edges).toHaveLength(0);
    expect(l.width).toBe(0);
    expect(l.height).toBe(0);
  });

  test("a single node is positioned with a non-empty viewport", () => {
    const l = computeDagLayout([{ id: 1, label: "solo" }], []);
    expect(l.nodes).toHaveLength(1);
    expect(l.nodes[0].width).toBeGreaterThan(0);
    expect(l.width).toBeGreaterThan(0);
    expect(l.height).toBeGreaterThan(0);
  });

  test("a chain lays out left-to-right by longest-path layer", () => {
    const l = computeDagLayout(
      [
        { id: 1, label: "A" },
        { id: 2, label: "B" },
        { id: 3, label: "C" },
      ],
      [
        { fromId: 1, toId: 2 },
        { fromId: 2, toId: 3 },
      ],
    );
    const x = (id: number) => l.nodes.find((n) => n.id === id)!.x;
    expect(x(1)).toBeLessThan(x(2));
    expect(x(2)).toBeLessThan(x(3));
    expect(l.edges).toHaveLength(2);
    // Each edge starts at the right edge of its source node.
    for (const e of l.edges) expect(e.pathData.startsWith("M ")).toBe(true);
  });

  test("isolated nodes are pushed to their own rightmost layer", () => {
    const l = computeDagLayout(
      [
        { id: 1, label: "A" },
        { id: 2, label: "B" },
        { id: 9, label: "loner" },
      ],
      [{ fromId: 1, toId: 2 }],
    );
    const x = (id: number) => l.nodes.find((n) => n.id === id)!.x;
    // loner has no edges, so it sits to the right of the connected B node.
    expect(x(9)).toBeGreaterThan(x(2));
  });

  test("ignores edges that reference unknown nodes", () => {
    const l = computeDagLayout(
      [{ id: 1, label: "A" }],
      [{ fromId: 1, toId: 999 }],
    );
    expect(l.edges).toHaveLength(0);
    expect(l.nodes).toHaveLength(1);
  });

  test("a cycle does not throw and still positions every node", () => {
    const l = computeDagLayout(
      [
        { id: 1, label: "A" },
        { id: 2, label: "B" },
      ],
      [
        { fromId: 1, toId: 2 },
        { fromId: 2, toId: 1 },
      ],
    );
    expect(l.nodes).toHaveLength(2);
  });

  test("preserves the highlighted flag", () => {
    const l = computeDagLayout([{ id: 1, label: "A", isHighlighted: true }], []);
    expect(l.nodes[0].isHighlighted).toBe(true);
  });
});
