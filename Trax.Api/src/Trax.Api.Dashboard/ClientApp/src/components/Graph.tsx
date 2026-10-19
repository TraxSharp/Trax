import { useMemo } from "react";
import {
  BaseEdge,
  EdgeLabelRenderer,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  useReactFlow,
  type Edge,
  type EdgeProps,
  type Node,
  type NodeProps,
} from "@xyflow/react";
import "@xyflow/react/dist/base.css";
import { Maximize, Minus, Plus } from "lucide-react";
import {
  layoutGraph,
  pathThrough,
  type GraphDirection,
  type GraphEdgeSpec,
  type GraphNodeSpec,
  type GraphTone,
  type LaidOutEdge,
  type LaidOutNode,
} from "../lib/graphLayout";

// The one component every graph in the console is drawn with (api/0044): a run's chain, a
// train's chain, a machine's states, manifest-group dependencies. dagre lays it out
// (lib/graphLayout.ts); React Flow draws it, pans and zooms. Nodes are positioned from the layout
// and never dragged, so the edges keep the route dagre gave them.

const NODE_TONE: Record<GraphTone, string> = {
  idle: "border-line-strong bg-surface text-fg",
  ok: "border-ok-line bg-ok-soft text-ok-fg",
  info: "border-info-line bg-info-soft text-info-fg",
  warn: "border-warn-line bg-warn-soft text-warn-fg",
  danger: "border-danger-line bg-danger-soft text-danger-fg",
  accent: "border-accent bg-accent-soft text-accent-fg",
};

const BADGE_TONE: Record<GraphTone, string> = {
  idle: "bg-sunken text-muted",
  ok: "bg-ok text-on-accent",
  info: "bg-info text-on-accent",
  warn: "bg-warn text-on-warn",
  danger: "bg-danger text-on-accent",
  accent: "bg-accent text-on-accent",
};

const STROKE: Record<GraphTone, string> = {
  idle: "var(--line-strong)",
  ok: "var(--ok)",
  info: "var(--info)",
  warn: "var(--warn)",
  danger: "var(--danger)",
  accent: "var(--accent)",
};

export interface GraphLegendEntry {
  label: string;
  tone: GraphTone;
}

type NodeData = {
  spec: LaidOutNode;
  selected: boolean;
  direction: GraphDirection;
  onSelect?: (id: string) => void;
};
type EdgeData = { spec: LaidOutEdge };

function TraxNode({ data }: NodeProps<Node<NodeData>>) {
  const { spec, selected, direction, onSelect } = data;
  const tone = NODE_TONE[spec.tone ?? "idle"];
  const [into, out] = direction === "LR" ? [Position.Left, Position.Right] : [Position.Top, Position.Bottom];
  const body = (
    <>
      <span className="block truncate text-[13px] font-medium leading-5" title={spec.label}>
        {spec.label}
      </span>
      {spec.detail && (
        <span className="block truncate text-[11px] leading-4 opacity-75" title={spec.detail}>
          {spec.detail}
        </span>
      )}
      {spec.badges && spec.badges.length > 0 && (
        <span className="mt-1 flex justify-center gap-1">
          {spec.badges.map((b) => (
            <span
              key={`${b.label}-${b.title ?? ""}`}
              className={`rounded-full px-1.5 text-[11px] font-medium leading-4 ${BADGE_TONE[b.tone]}`}
              title={b.title}
              aria-label={b.title ? `${b.title}: ${b.label}` : undefined}
            >
              {b.label}
            </span>
          ))}
        </span>
      )}
    </>
  );
  const frame = [
    "h-full w-full rounded-lg border px-3 py-1.5 text-center",
    tone,
    spec.final ? "outline outline-1 outline-offset-2 outline-current" : "",
    selected ? "ring-2 ring-accent ring-offset-1 ring-offset-canvas" : "",
  ].join(" ");
  return (
    <>
      <Handle type="target" position={into} isConnectable={false} className="opacity-0" />
      {spec.initial && (
        <span
          aria-hidden
          className={`absolute h-2.5 w-2.5 rounded-full bg-fg ${
            direction === "LR" ? "-left-5 top-1/2 -translate-y-1/2" : "-top-5 left-1/2 -translate-x-1/2"
          }`}
        />
      )}
      {onSelect ? (
        <button
          type="button"
          className={`${frame} cursor-pointer hover:border-accent focus-visible:outline-2 focus-visible:outline-accent`}
          aria-pressed={selected}
          aria-current={spec.current ? "step" : undefined}
          onClick={() => onSelect(spec.id)}
        >
          {body}
        </button>
      ) : (
        <div className={frame} aria-current={spec.current ? "step" : undefined}>
          {body}
        </div>
      )}
      <Handle type="source" position={out} isConnectable={false} className="opacity-0" />
    </>
  );
}

function TraxEdge({ id, data, markerEnd }: EdgeProps<Edge<EdgeData>>) {
  const spec = data!.spec;
  const stroke = STROKE[spec.tone ?? "idle"];
  return (
    <>
      <BaseEdge
        id={id}
        path={pathThrough(spec.points)}
        markerEnd={markerEnd}
        style={{ stroke, strokeWidth: 1.5, strokeDasharray: spec.dashed ? "5 4" : undefined }}
      />
      {spec.label && spec.labelAt && (
        <EdgeLabelRenderer>
          <span
            className="nodrag nopan absolute rounded bg-canvas px-1 text-[11px] leading-4 text-muted"
            style={{
              transform: `translate(-50%, -50%) translate(${spec.labelAt.x}px, ${spec.labelAt.y}px)`,
            }}
          >
            {spec.label}
          </span>
        </EdgeLabelRenderer>
      )}
    </>
  );
}

const nodeTypes = { trax: TraxNode };
const edgeTypes = { trax: TraxEdge };

function ZoomControls() {
  const flow = useReactFlow();
  const button =
    "flex h-7 w-7 items-center justify-center text-muted hover:bg-hover hover:text-fg focus-visible:outline-2 focus-visible:outline-accent";
  return (
    <div className="absolute bottom-2 left-2 z-10 flex flex-col overflow-hidden rounded-md border border-line bg-surface">
      <button type="button" className={button} aria-label="Zoom in" onClick={() => flow.zoomIn()}>
        <Plus size={14} />
      </button>
      <button type="button" className={button} aria-label="Zoom out" onClick={() => flow.zoomOut()}>
        <Minus size={14} />
      </button>
      <button
        type="button"
        className={button}
        aria-label="Fit to view"
        onClick={() => flow.fitView({ padding: 0.1 })}
      >
        <Maximize size={14} />
      </button>
    </div>
  );
}

export function Graph({
  nodes,
  edges,
  ariaLabel,
  direction = "LR",
  selectedId,
  onSelect,
  legend,
  emptyText = "Nothing to draw.",
  minHeight = 160,
  maxHeight = 560,
}: {
  nodes: GraphNodeSpec[];
  edges: GraphEdgeSpec[];
  /** What the graph shows, for screen readers ("Dependency graph", "States of Approval"). */
  ariaLabel: string;
  direction?: GraphDirection;
  selectedId?: string;
  /** When given, each node is a button that selects it. */
  onSelect?: (id: string) => void;
  legend?: GraphLegendEntry[];
  emptyText?: string;
  minHeight?: number;
  maxHeight?: number;
}) {
  const layout = useMemo(() => layoutGraph(nodes, edges, direction), [nodes, edges, direction]);

  const flowNodes = useMemo<Node<NodeData>[]>(() => {
    const [into, out] = direction === "LR" ? [Position.Left, Position.Right] : [Position.Top, Position.Bottom];
    return layout.nodes.map((n) => ({
      id: n.id,
      type: "trax",
      position: { x: n.x, y: n.y },
      width: n.width,
      height: n.height,
      // Known up front, so edges draw without measuring the DOM first (and in tests).
      handles: [
        { type: "target", position: into, ...anchor(into, n) },
        { type: "source", position: out, ...anchor(out, n) },
      ],
      draggable: false,
      connectable: false,
      selectable: false,
      // React Flow makes a node it thinks inert ignore the pointer; a selectable one holds a button.
      style: onSelect ? { pointerEvents: "all" as const } : undefined,
      data: { spec: n, selected: n.id === selectedId, direction, onSelect },
    }));
  }, [layout, selectedId, direction, onSelect]);

  const flowEdges = useMemo<Edge<EdgeData>[]>(
    () =>
      layout.edges.map((e) => ({
        id: e.id,
        source: e.source,
        target: e.target,
        type: "trax",
        selectable: false,
        markerEnd: { type: MarkerType.ArrowClosed, width: 16, height: 16, color: STROKE[e.tone ?? "idle"] },
        data: { spec: e },
      })),
    [layout],
  );

  if (layout.nodes.length === 0)
    return <p className="py-8 text-center text-sm text-muted">{emptyText}</p>;

  const height = Math.min(maxHeight, Math.max(minHeight, layout.height + 16));
  return (
    <div>
      <div
        role="group"
        aria-label={ariaLabel}
        className="trax-graph relative rounded-md border border-line bg-canvas"
        style={{ height }}
      >
        <ReactFlow
          nodes={flowNodes}
          edges={flowEdges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          fitView
          fitViewOptions={{ padding: 0.1, maxZoom: 1 }}
          minZoom={0.25}
          maxZoom={2}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable={false}
          zoomOnDoubleClick={false}
          preventScrolling={false}
        >
          <ZoomControls />
        </ReactFlow>
      </div>
      {legend && legend.length > 0 && (
        <ul className="mt-2 flex flex-wrap gap-3 text-xs text-muted" aria-label="Legend">
          {legend.map((l) => (
            <li key={l.label} className="flex items-center gap-1.5">
              <span aria-hidden className={`h-3 w-3 rounded-sm border ${NODE_TONE[l.tone]}`} />
              {l.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function anchor(side: Position, n: LaidOutNode) {
  switch (side) {
    case Position.Left:
      return { x: 0, y: n.height / 2, width: 1, height: 1 };
    case Position.Right:
      return { x: n.width, y: n.height / 2, width: 1, height: 1 };
    case Position.Top:
      return { x: n.width / 2, y: 0, width: 1, height: 1 };
    default:
      return { x: n.width / 2, y: n.height, width: 1, height: 1 };
  }
}
