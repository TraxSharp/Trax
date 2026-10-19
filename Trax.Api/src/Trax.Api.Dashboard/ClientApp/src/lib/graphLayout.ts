// Layered layout for every graph the console draws (api/0044): dagre places the nodes and routes
// the edges, cycles included, so a machine's loop and a train's chain go through the same code.
// dagre breaks a cycle by reversing an edge while it lays out and restores it afterwards, so the
// points it returns already run from the edge's source to its target.
import dagre from "@dagrejs/dagre";

/** The colour a node, badge or edge takes; each maps to the theme's tokens in Graph.tsx. */
export type GraphTone = "idle" | "ok" | "info" | "warn" | "danger" | "accent";

export interface GraphBadge {
  label: string;
  tone: GraphTone;
  /** What the badge counts, for the tooltip and the accessible name. */
  title?: string;
}

export interface GraphNodeSpec {
  id: string;
  label: string;
  /** A second, quieter line under the label. */
  detail?: string;
  tone?: GraphTone;
  badges?: GraphBadge[];
  /** A machine's initial state is drawn with an entry marker, a final one with a double border. */
  initial?: boolean;
  final?: boolean;
  /** Where something is now (an instance's state): announced as the current step. */
  current?: boolean;
}

export interface GraphEdgeSpec {
  id: string;
  source: string;
  target: string;
  label?: string;
  dashed?: boolean;
  tone?: GraphTone;
}

export interface Point {
  x: number;
  y: number;
}

export interface LaidOutNode extends GraphNodeSpec {
  /** Top-left corner. */
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface LaidOutEdge extends GraphEdgeSpec {
  /** From the source's border to the target's, in order. */
  points: Point[];
  /** The centre of the label, when the edge has one. */
  labelAt?: Point;
}

export interface GraphLayout {
  nodes: LaidOutNode[];
  edges: LaidOutEdge[];
  width: number;
  height: number;
}

export type GraphDirection = "LR" | "TB";

const CHAR_WIDTH = 7.2; // 13px Geist, measured on the widest common labels
const MIN_NODE_WIDTH = 120;
const MAX_NODE_WIDTH = 260;
const NODE_HEIGHT = 40;
const DETAIL_HEIGHT = 16;
const BADGE_ROW_HEIGHT = 20;
const LABEL_CHAR_WIDTH = 6.4; // 11px edge labels
const PADDING = 24;

function nodeSize(n: GraphNodeSpec): { width: number; height: number } {
  const text = Math.max(n.label.length, (n.detail ?? "").length * 0.9);
  const width = Math.min(MAX_NODE_WIDTH, Math.max(MIN_NODE_WIDTH, text * CHAR_WIDTH + 32));
  const height =
    NODE_HEIGHT + (n.detail ? DETAIL_HEIGHT : 0) + (n.badges?.length ? BADGE_ROW_HEIGHT : 0);
  return { width, height };
}

/**
 * Lays out the graph. Edges naming a node that is not in `nodes` are dropped, so a caller can pass
 * a partial view. Several edges between one pair (two commands into the same state) are kept apart.
 */
export function layoutGraph(
  nodes: GraphNodeSpec[],
  edges: GraphEdgeSpec[],
  direction: GraphDirection = "LR",
): GraphLayout {
  if (nodes.length === 0) return { nodes: [], edges: [], width: 0, height: 0 };

  const g = new dagre.graphlib.Graph({ multigraph: true });
  g.setGraph({
    rankdir: direction,
    nodesep: 32,
    ranksep: 72,
    edgesep: 16,
    marginx: PADDING,
    marginy: PADDING,
  });
  g.setDefaultEdgeLabel(() => ({}));

  const known = new Set<string>();
  for (const n of nodes) {
    known.add(n.id);
    g.setNode(n.id, { ...nodeSize(n) });
  }
  const kept = edges.filter((e) => known.has(e.source) && known.has(e.target));
  for (const e of kept) {
    g.setEdge(
      e.source,
      e.target,
      e.label
        ? { width: e.label.length * LABEL_CHAR_WIDTH + 12, height: 16, labelpos: "c" }
        : { width: 0, height: 0 },
      e.id,
    );
  }

  dagre.layout(g);

  const laidOut: LaidOutNode[] = nodes.map((n) => {
    const p = g.node(n.id);
    return { ...n, x: p.x - p.width / 2, y: p.y - p.height / 2, width: p.width, height: p.height };
  });
  const laidOutEdges: LaidOutEdge[] = kept.map((e) => {
    const p = g.edge(e.source, e.target, e.id) as {
      points?: Point[];
      x?: number;
      y?: number;
    };
    return {
      ...e,
      points: (p.points ?? []).map(({ x, y }) => ({ x, y })),
      labelAt: e.label && p.x !== undefined && p.y !== undefined ? { x: p.x, y: p.y } : undefined,
    };
  });
  const box = g.graph();
  return {
    nodes: laidOut,
    edges: laidOutEdges,
    width: box.width ?? 0,
    height: box.height ?? 0,
  };
}

/**
 * An SVG path through the points: straight for two, otherwise a curve that passes through each
 * point's neighbourhood (quadratic segments between midpoints), so a routed edge reads as one line.
 */
export function pathThrough(points: Point[]): string {
  if (points.length === 0) return "";
  const [first, ...rest] = points;
  if (rest.length === 0) return `M ${first.x} ${first.y}`;
  if (rest.length === 1) return `M ${first.x} ${first.y} L ${rest[0].x} ${rest[0].y}`;
  let d = `M ${first.x} ${first.y}`;
  for (let i = 1; i < points.length - 1; i++) {
    const p = points[i];
    const next = points[i + 1];
    const mid = { x: (p.x + next.x) / 2, y: (p.y + next.y) / 2 };
    d += i === points.length - 2 ? ` Q ${p.x} ${p.y} ${next.x} ${next.y}` : ` Q ${p.x} ${p.y} ${mid.x} ${mid.y}`;
  }
  return d;
}
