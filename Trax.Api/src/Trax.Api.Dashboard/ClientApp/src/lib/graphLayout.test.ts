import { describe, expect, test } from "vitest";
import { layoutGraph, pathThrough, type GraphEdgeSpec, type GraphNodeSpec } from "./graphLayout";

const node = (id: string): GraphNodeSpec => ({ id, label: id });
const edge = (source: string, target: string, label?: string): GraphEdgeSpec => ({
  id: `${source}-${target}-${label ?? ""}`,
  source,
  target,
  label,
});

describe("layoutGraph", () => {
  test("an empty graph has an empty layout", () => {
    expect(layoutGraph([], [])).toEqual({ nodes: [], edges: [], width: 0, height: 0 });
  });

  test("a chain is laid out left to right", () => {
    const l = layoutGraph([node("a"), node("b"), node("c")], [edge("a", "b"), edge("b", "c")]);
    const x = (id: string) => l.nodes.find((n) => n.id === id)!.x;
    expect(x("a")).toBeLessThan(x("b"));
    expect(x("b")).toBeLessThan(x("c"));
    expect(l.width).toBeGreaterThan(0);
    expect(l.height).toBeGreaterThan(0);
  });

  test("a top-to-bottom layout stacks the chain", () => {
    const l = layoutGraph([node("a"), node("b")], [edge("a", "b")], "TB");
    const [a, b] = l.nodes;
    expect(a.y).toBeLessThan(b.y);
  });

  // The hand-written layered layout this replaced refused this graph; every machine with a loop is one.
  test("a three-state cycle places every node and routes every edge", () => {
    const l = layoutGraph(
      [node("Idle"), node("Running"), node("Done")],
      [edge("Idle", "Running", "start"), edge("Running", "Done", "finish"), edge("Done", "Idle", "reset")],
    );
    expect(l.nodes.every((n) => Number.isFinite(n.x) && Number.isFinite(n.y))).toBe(true);
    expect(l.edges).toHaveLength(3);
    for (const e of l.edges) expect(e.points.length).toBeGreaterThanOrEqual(2);
  });

  test("a reversed edge still runs from its source to its target", () => {
    const l = layoutGraph(
      [node("a"), node("b")],
      [edge("a", "b", "go"), edge("b", "a", "back")],
    );
    const nodeAt = (id: string) => l.nodes.find((n) => n.id === id)!;
    for (const e of l.edges) {
      const s = nodeAt(e.source);
      const t = nodeAt(e.target);
      const first = e.points[0];
      const last = e.points[e.points.length - 1];
      const dist = (p: { x: number; y: number }, n: typeof s) =>
        Math.hypot(p.x - (n.x + n.width / 2), p.y - (n.y + n.height / 2));
      expect(dist(first, s)).toBeLessThan(dist(first, t));
      expect(dist(last, t)).toBeLessThan(dist(last, s));
    }
  });

  test("two commands between the same states are two edges, each with its label placed", () => {
    const l = layoutGraph(
      [node("a"), node("b")],
      [edge("a", "b", "approve"), edge("a", "b", "skip")],
    );
    expect(l.edges).toHaveLength(2);
    expect(l.edges.every((e) => e.labelAt !== undefined)).toBe(true);
  });

  test("a self-transition is routed", () => {
    const l = layoutGraph([node("a")], [edge("a", "a", "retry")]);
    expect(l.edges).toHaveLength(1);
    expect(l.edges[0].points.length).toBeGreaterThanOrEqual(2);
  });

  test("an edge naming a node that is not drawn is dropped", () => {
    const l = layoutGraph([node("a")], [edge("a", "missing")]);
    expect(l.edges).toHaveLength(0);
  });

  test("a node with badges and a detail line is taller", () => {
    const l = layoutGraph(
      [node("plain"), { id: "busy", label: "busy", detail: "Invokes a train", badges: [{ label: "3", tone: "info" }] }],
      [],
    );
    const plain = l.nodes.find((n) => n.id === "plain")!;
    const busy = l.nodes.find((n) => n.id === "busy")!;
    expect(busy.height).toBeGreaterThan(plain.height);
  });
});

describe("pathThrough", () => {
  test("two points are a straight line", () => {
    expect(pathThrough([{ x: 0, y: 0 }, { x: 10, y: 5 }])).toBe("M 0 0 L 10 5");
  });

  test("more points are one curve that ends at the last point", () => {
    const d = pathThrough([{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }, { x: 20, y: 10 }]);
    expect(d.startsWith("M 0 0")).toBe(true);
    expect(d.endsWith("20 10")).toBe(true);
    expect(d).not.toContain("L");
  });
});
