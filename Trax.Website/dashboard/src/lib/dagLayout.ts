// Layered ("Sugiyama"-style) DAG layout: longest-path layer assignment, a barycenter sweep to
// reduce edge crossings, then cubic-bezier edge routing. Ported from the Blazor dashboard's
// DagLayoutEngine so the React and Blazor dependency graphs lay out identically.

export interface DagNode {
  id: number;
  label: string;
  isHighlighted?: boolean;
}

export interface DagEdge {
  fromId: number;
  toId: number;
}

export interface PositionedNode {
  id: number;
  label: string;
  isHighlighted: boolean;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface PositionedEdge {
  fromId: number;
  toId: number;
  /** SVG cubic bezier path data. */
  pathData: string;
}

export interface DagLayout {
  nodes: PositionedNode[];
  edges: PositionedEdge[];
  width: number;
  height: number;
}

const NODE_WIDTH = 180;
const NODE_HEIGHT = 40;
const LAYER_GAP = 120;
const NODE_GAP = 24;
const PADDING = 40;

/** Kahn's algorithm. Returns the topological order and whether the graph is acyclic. */
function topologicalSort(
  nodeIds: number[],
  edges: DagEdge[],
): { sorted: number[]; isAcyclic: boolean } {
  const nodeSet = new Set(nodeIds);
  const successors = new Map<number, number[]>();
  const inDegree = new Map<number, number>();
  for (const id of nodeSet) {
    successors.set(id, []);
    inDegree.set(id, 0);
  }
  for (const e of edges) {
    if (!nodeSet.has(e.fromId) || !nodeSet.has(e.toId)) continue;
    successors.get(e.fromId)!.push(e.toId);
    inDegree.set(e.toId, (inDegree.get(e.toId) ?? 0) + 1);
  }

  const queue = [...nodeSet].filter((n) => inDegree.get(n) === 0);
  const sorted: number[] = [];
  while (queue.length > 0) {
    const current = queue.shift()!;
    sorted.push(current);
    for (const succ of successors.get(current)!) {
      inDegree.set(succ, inDegree.get(succ)! - 1);
      if (inDegree.get(succ) === 0) queue.push(succ);
    }
  }
  return { sorted, isAcyclic: sorted.length === nodeSet.size };
}

function positionIndex(layerNodes: DagNode[]): Map<number, number> {
  const index = new Map<number, number>();
  layerNodes.forEach((n, i) => index.set(n.id, i));
  return index;
}

// Average position of a node's neighbours in an adjacent layer. Neighbourless nodes sort last.
function barycenter(
  nodeId: number,
  adjacency: Map<number, number[]>,
  neighborPositions: Map<number, number>,
): number {
  const neighbors = adjacency.get(nodeId) ?? [];
  let sum = 0;
  let count = 0;
  for (const n of neighbors) {
    const pos = neighborPositions.get(n);
    if (pos !== undefined) {
      sum += pos;
      count++;
    }
  }
  return count > 0 ? sum / count : Number.MAX_VALUE;
}

export function computeDagLayout(nodes: DagNode[], edges: DagEdge[]): DagLayout {
  if (nodes.length === 0) return { nodes: [], edges: [], width: 0, height: 0 };

  const nodeIds = nodes.map((n) => n.id);
  const idSet = new Set(nodeIds);
  const successors = new Map<number, number[]>(nodeIds.map((id) => [id, []]));
  const predecessors = new Map<number, number[]>(nodeIds.map((id) => [id, []]));

  const validEdges = edges.filter((e) => idSet.has(e.fromId) && idSet.has(e.toId));
  for (const e of validEdges) {
    successors.get(e.fromId)!.push(e.toId);
    predecessors.get(e.toId)!.push(e.fromId);
  }

  const sortResult = topologicalSort(nodeIds, validEdges);
  const sorted = sortResult.isAcyclic ? sortResult.sorted : nodeIds;

  // Longest-path layer assignment.
  const layer = new Map<number, number>();
  for (const id of sorted) {
    const preds = predecessors.get(id)!;
    if (preds.length === 0) {
      layer.set(id, 0);
    } else {
      const known = preds.filter((p) => layer.has(p)).map((p) => layer.get(p)!);
      layer.set(id, (known.length ? Math.max(...known) : -1) + 1);
    }
  }

  // Isolated nodes (no edges) go into their own rightmost layer.
  const isolated = new Set(
    nodes
      .filter((n) => successors.get(n.id)!.length === 0 && predecessors.get(n.id)!.length === 0)
      .map((n) => n.id),
  );
  const connectedLayers = [...layer.entries()]
    .filter(([id]) => !isolated.has(id))
    .map(([, l]) => l);
  const maxConnectedLayer = connectedLayers.length ? Math.max(...connectedLayers) : -1;
  if (isolated.size > 0) {
    for (const id of isolated) layer.set(id, maxConnectedLayer + 1);
  }

  // Group by layer, alphabetical within a layer to start.
  const layerGroups = new Map<number, DagNode[]>();
  for (const n of nodes) {
    const l = layer.get(n.id)!;
    if (!layerGroups.has(l)) layerGroups.set(l, []);
    layerGroups.get(l)!.push(n);
  }
  for (const g of layerGroups.values()) g.sort((a, b) => a.label.localeCompare(b.label));

  // Barycenter heuristic: two full sweeps to reduce edge crossings.
  const layerKeys = [...layerGroups.keys()].sort((a, b) => a - b);
  const byLabel = (a: DagNode, b: DagNode) => a.label.localeCompare(b.label);
  for (let sweep = 0; sweep < 2; sweep++) {
    for (let li = 1; li < layerKeys.length; li++) {
      const prevOrder = positionIndex(layerGroups.get(layerKeys[li - 1])!);
      layerGroups.get(layerKeys[li])!.sort((a, b) => {
        const d = barycenter(a.id, predecessors, prevOrder) - barycenter(b.id, predecessors, prevOrder);
        return d !== 0 ? d : byLabel(a, b);
      });
    }
    for (let li = layerKeys.length - 2; li >= 0; li--) {
      const nextOrder = positionIndex(layerGroups.get(layerKeys[li + 1])!);
      layerGroups.get(layerKeys[li])!.sort((a, b) => {
        const d = barycenter(a.id, successors, nextOrder) - barycenter(b.id, successors, nextOrder);
        return d !== 0 ? d : byLabel(a, b);
      });
    }
  }

  const maxNodesInLayer = Math.max(...[...layerGroups.values()].map((g) => g.length));
  const maxLayerHeight = maxNodesInLayer * (NODE_HEIGHT + NODE_GAP) - NODE_GAP;

  const positioned = new Map<number, PositionedNode>();
  for (const [layerIndex, nodesInLayer] of layerGroups) {
    const layerHeight = nodesInLayer.length * (NODE_HEIGHT + NODE_GAP) - NODE_GAP;
    const yOffset = (maxLayerHeight - layerHeight) / 2;
    nodesInLayer.forEach((n, i) => {
      positioned.set(n.id, {
        id: n.id,
        label: n.label,
        isHighlighted: n.isHighlighted ?? false,
        x: PADDING + layerIndex * (NODE_WIDTH + LAYER_GAP),
        y: PADDING + yOffset + i * (NODE_HEIGHT + NODE_GAP),
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
      });
    });
  }

  const positionedEdges: PositionedEdge[] = validEdges.map((e) => {
    const from = positioned.get(e.fromId)!;
    const to = positioned.get(e.toId)!;
    const startX = from.x + NODE_WIDTH;
    const startY = from.y + NODE_HEIGHT / 2;
    const endX = to.x;
    const endY = to.y + NODE_HEIGHT / 2;
    const dx = endX - startX;
    const cp1X = startX + dx * 0.4;
    const cp2X = endX - dx * 0.4;
    const f = (v: number) => v.toFixed(1);
    return {
      fromId: e.fromId,
      toId: e.toId,
      pathData: `M ${f(startX)},${f(startY)} C ${f(cp1X)},${f(startY)} ${f(cp2X)},${f(endY)} ${f(endX)},${f(endY)}`,
    };
  });

  const maxLayer = Math.max(...layerKeys);
  return {
    nodes: [...positioned.values()],
    edges: positionedEdges,
    width: PADDING * 2 + (maxLayer + 1) * NODE_WIDTH + maxLayer * LAYER_GAP,
    height: PADDING * 2 + maxLayerHeight,
  };
}
