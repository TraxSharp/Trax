import { expect, test } from "vitest";
import { chainGraph, failuresByStep, stepName, stepRows } from "./trainChain";
import type { TrainChainNode } from "../types";

function node(id: string, fields: Partial<TrainChainNode> = {}): TrainChainNode {
  const slash = id.lastIndexOf("/");
  const parent = slash < 0 ? null : id.slice(0, id.lastIndexOf("/", slash - 1));
  const track = slash < 0 ? null : id.slice(id.lastIndexOf("/", slash - 1) + 1, slash);
  return {
    id,
    kind: "CHAIN",
    junction: stepName(id),
    opaque: false,
    state: "NOT_REACHED",
    parentId: parent,
    track,
    depth: id.split("/").length > 1 ? (id.split("/").length - 1) / 2 : 0,
    tracks: [],
    ...fields,
  };
}

// Load, then a Switch with a Short track (one step) and a Long track (two), then Publish.
const CHAIN: TrainChainNode[] = [
  node("Load#0"),
  node("Route#1", {
    kind: "SWITCH",
    junction: null,
    tracks: [
      { name: "Short", description: null, isFallback: false, taken: false },
      { name: "Long", description: null, isFallback: true, taken: false },
    ],
  }),
  node("Route#1/Short/Trim#0"),
  node("Route#1/Long/Split#0"),
  node("Route#1/Long/Join#1"),
  node("Publish#2"),
];

test("a step's name is the last segment of its id without its index", () => {
  expect(stepName("Route#1/Long/Split#0")).toBe("Split");
  expect(stepName("Load#0")).toBe("Load");
});

test("the chain fans out to each track and every track joins the step after it", () => {
  const { nodes, edges } = chainGraph(CHAIN);
  expect(nodes.map((n) => n.label)).toEqual(["Load", "Route", "Trim", "Split", "Join", "Publish"]);
  expect(edges.map((e) => [e.source, e.target, e.label])).toEqual([
    ["Load#0", "Route#1", undefined],
    ["Route#1", "Route#1/Short/Trim#0", "Short"],
    ["Route#1", "Route#1/Long/Split#0", "Long"],
    ["Route#1/Long/Split#0", "Route#1/Long/Join#1", undefined],
    ["Route#1/Short/Trim#0", "Publish#2", undefined],
    ["Route#1/Long/Join#1", "Publish#2", undefined],
  ]);
  expect(nodes[1].detail).toBe("Switch");
});

test("a track with no steps passes through its step", () => {
  const { edges } = chainGraph([
    node("Gate#0", { kind: "GATE", tracks: [{ name: "Yes", description: null, isFallback: false, taken: false }] }),
    node("Next#1"),
  ]);
  expect(edges.map((e) => e.id)).toEqual(["Gate#0->Next#1"]);
});

test("a failure is counted on the one step running its junction, and the rest apart", () => {
  const { byStep, elsewhere } = failuresByStep(
    [...CHAIN, node("Again#3", { junction: "Split" })],
    [
      { failureJunction: "Trim" },
      { failureJunction: "Trim" },
      { failureJunction: "Branch:Publish" },
      { failureJunction: "TrainException" },
      { failureJunction: "Split" }, // two steps run Split
      { failureJunction: null },
    ],
  );
  expect(Object.fromEntries(byStep)).toEqual({ "Route#1/Short/Trim#0": 2, "Publish#2": 1 });
  expect(elsewhere).toBe(3);
});

test("a step with failures carries a badge that says what it counts", () => {
  const { nodes } = chainGraph(CHAIN, new Map([["Publish#2", 4]]), "Failed runs in the last 24 hours");
  const publish = nodes.find((n) => n.id === "Publish#2")!;
  expect(publish.tone).toBe("danger");
  expect(publish.badges).toEqual([{ label: "4", tone: "danger", title: "Failed runs in the last 24 hours" }]);
  expect(nodes.find((n) => n.id === "Load#0")!.badges).toBeUndefined();
});

test("the grid has a row per step at any depth, in declared order", () => {
  expect(stepRows(CHAIN).map((r) => [r.label, r.depth])).toEqual([
    ["Load", 0],
    ["Route", 0],
    ["Trim", 1],
    ["Split", 1],
    ["Join", 1],
    ["Publish", 0],
  ]);
});
