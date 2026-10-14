import { expect, test } from "vitest";
import { flattenRunGraph, runGraphTree } from "./runGraphTree";
import type { ChainStepKind, RunGraphFlatNode, RunGraphNode } from "../types";

function node(id: string, kind: ChainStepKind, tracks: RunGraphNode["tracks"] = []): RunGraphNode {
  return { id, kind, opaque: false, replayed: false, checkpointed: false, canResume: false, state: "NOT_REACHED", steps: [], tracks };
}

// A deterministic pseudo-random tree: up to `depth` levels of routing and Parallel steps, each with
// one to three tracks of one to three nodes.
function randomTree(seed: number, depth: number, prefix = ""): RunGraphNode[] {
  let s = seed;
  const next = (n: number) => {
    s = (s * 1103515245 + 12345) % 2147483648;
    return Math.floor(s / 65536) % n;
  };
  const count = 1 + next(3);
  return Array.from({ length: count }, (_, i) => {
    const id = `${prefix}N${i}#${i}`;
    if (depth === 0 || next(3) === 0) return node(id, "CHAIN");
    const trackCount = 1 + next(3);
    return node(
      id,
      next(2) === 0 ? "PARALLEL" : "DECIDE",
      Array.from({ length: trackCount }, (_, t) => ({
        name: `T${t}`,
        description: null,
        isFallback: false,
        taken: t === 0,
        nodes: randomTree(seed * 31 + i * 7 + t, depth - 1, `${id}/T${t}/`),
      })),
    );
  });
}

const maxDepth = (nodes: RunGraphFlatNode[]) => Math.max(...nodes.map((n) => n.depth));

test("the tree rebuilt from the flat list is the tree it was flattened from, at any depth", () => {
  for (let seed = 1; seed <= 200; seed++) {
    const tree = randomTree(seed, 6);
    const flat = flattenRunGraph(tree);
    expect(runGraphTree(flat)).toEqual(tree);
  }
});

test("some generated trees go deeper than a query can follow nested tracks", () => {
  const depths = Array.from({ length: 200 }, (_, i) => maxDepth(flattenRunGraph(randomTree(i + 1, 6))));
  expect(Math.max(...depths)).toBeGreaterThanOrEqual(5);
});

test("the flat list carries each node's parent, track and depth, depth first", () => {
  const tree = [node("A#0", "DECIDE", [{ name: "x", description: null, isFallback: false, taken: true, nodes: [node("A#0/x/B#0", "CHAIN")] }]), node("C#1", "CHAIN")];
  expect(flattenRunGraph(tree).map((n) => [n.id, n.parentId, n.track, n.depth])).toEqual([
    ["A#0", null, null, 0],
    ["A#0/x/B#0", "A#0", "x", 1],
    ["C#1", null, null, 0],
  ]);
});

test("a node whose parent or track the list does not carry is still drawn", () => {
  const flat = flattenRunGraph([node("A#0", "DECIDE")]);
  const orphan = { ...flattenRunGraph([node("Z#0", "CHAIN")])[0], parentId: "Gone#9", track: "t", depth: 1 };
  const unnamedTrack = { ...flattenRunGraph([node("A#0/y/B#0", "CHAIN")])[0], parentId: "A#0", track: "y", depth: 1 };
  const tree = runGraphTree([...flat, orphan, unnamedTrack]);
  expect(tree.map((n) => n.id)).toEqual(["A#0", "Z#0"]);
  expect(tree[0].tracks.map((t) => [t.name, t.nodes.map((n) => n.id)])).toEqual([["y", ["A#0/y/B#0"]]]);
});
