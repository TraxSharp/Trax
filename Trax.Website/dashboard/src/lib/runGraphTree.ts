import type { RunGraphFlatNode, RunGraphNode, RunGraphTrack } from "../types";

// The run graph arrives as one flat list (runGraph.allNodes), because a query cannot follow nested
// tracks past the server's field-cycle limit. These rebuild the tree at any depth, and flatten one
// back into the list the API serves (for the mock).

/**
 * The declared tree of a run graph's flat list: the top-level nodes, each with its tracks and the
 * nodes on them, at any depth. A node keeps its place in the list's order on its track. A node whose
 * parent is not in the list sits at the top level, and one on a track its parent does not name gets
 * that track, so a node the list carries is never dropped.
 */
export function runGraphTree(allNodes: readonly RunGraphFlatNode[]): RunGraphNode[] {
  const built = new Map<string, RunGraphNode>();
  for (const flat of allNodes) built.set(flat.id, treeNode(flat));
  const top: RunGraphNode[] = [];
  for (const flat of allNodes) {
    const node = built.get(flat.id)!;
    const parent = flat.parentId != null ? built.get(flat.parentId) : undefined;
    if (!parent || parent === node || flat.track == null) {
      top.push(node);
      continue;
    }
    let track: RunGraphTrack | undefined = parent.tracks.find((t) => t.name === flat.track);
    if (!track) {
      track = { name: flat.track, description: null, isFallback: false, taken: false, nodes: [] };
      parent.tracks.push(track);
    }
    track.nodes.push(node);
  }
  return top;
}

/** The flat list of a declared tree, as runGraph.allNodes serves it: depth first, in declared order. */
export function flattenRunGraph(
  nodes: readonly RunGraphNode[],
  parentId: string | null = null,
  track: string | null = null,
  depth = 0,
): RunGraphFlatNode[] {
  return nodes.flatMap((node) => [
    {
      ...node,
      parentId,
      track,
      depth,
      tracks: node.tracks.map((t) => ({ name: t.name, description: t.description, isFallback: t.isFallback, taken: t.taken })),
    },
    ...node.tracks.flatMap((t) => flattenRunGraph(t.nodes, node.id, t.name, depth + 1)),
  ]);
}

// A tree node of a flat one, with its tracks and none of their nodes yet.
function treeNode(flat: RunGraphFlatNode): RunGraphNode {
  return {
    id: flat.id,
    kind: flat.kind,
    opaque: flat.opaque,
    replayed: flat.replayed,
    checkpointed: flat.checkpointed,
    canResume: flat.canResume,
    state: flat.state,
    steps: flat.steps,
    tracks: flat.tracks.map((t) => ({ ...t, nodes: [] })),
  };
}
