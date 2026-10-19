import type { GraphBadge, GraphEdgeSpec, GraphNodeSpec, GraphTone } from "./graphLayout";
import type { ChainStepKind, RunGraphFlatNode, RunGraphStep, RunNodeState } from "../types";

// A run's chain as the shared Graph component draws it (api/0044): one node per declared step, an
// edge from each step to the next, a routing step's tracks and a Parallel step's branches as
// branches that leave the step and join at the step after it.

/** How a node's state reads on the page. */
export const NODE_STATE_LABEL: Record<RunNodeState, string> = {
  NOT_REACHED: "Not reached",
  IN_PROGRESS: "Running",
  COMPLETED: "Completed",
  FAILED: "Failed",
  CANCELLED: "Cancelled",
  SKIPPED: "Skipped",
  NOT_RECORDED: "Not recorded",
  WITHHELD: "Withheld",
  RESTORED: "Restored",
  INTERRUPTED: "Interrupted",
};

export const NODE_STATE_TONE: Record<RunNodeState, GraphTone> = {
  NOT_REACHED: "idle",
  IN_PROGRESS: "info",
  COMPLETED: "ok",
  FAILED: "danger",
  CANCELLED: "warn",
  SKIPPED: "idle",
  NOT_RECORDED: "idle",
  WITHHELD: "idle",
  RESTORED: "accent",
  INTERRUPTED: "warn",
};

/** The legend under a run's graph: each colour once, by what it means. */
export const RUN_GRAPH_LEGEND: { label: string; tone: GraphTone }[] = [
  { label: "Completed", tone: "ok" },
  { label: "Running", tone: "info" },
  { label: "Failed", tone: "danger" },
  { label: "Cancelled or interrupted", tone: "warn" },
  { label: "Restored from a checkpoint", tone: "accent" },
  { label: "Not reached, skipped or not recorded", tone: "idle" },
];

/** How a step kind reads on the page. */
export function stepKindLabel(kind: ChainStepKind): string {
  switch (kind) {
    case "CHAIN":
      return "Junction";
    case "I_CHAIN":
      return "Junction (interface)";
    case "SHORT_CIRCUIT":
      return "Short circuit";
    default:
      return kind.charAt(0) + kind.slice(1).toLowerCase().replace(/_/g, " ");
  }
}

/** The step's own name: the last segment of its id without its position, as "Ship" for "Fast/Ship#0". */
export function nodeLabel(id: string): string {
  const slash = id.lastIndexOf("/");
  const own = slash < 0 ? id : id.slice(slash + 1);
  return own.replace(/#\d+$/, "");
}

/** The node a run's failure is read at: the first, depth first, whose own step failed or was cancelled. */
export function failedNode<T extends Pick<RunGraphFlatNode, "id" | "state"> & { steps: Pick<RunGraphStep, "state">[] }>(
  nodes: readonly T[],
): T | undefined {
  return (
    nodes.find((n) => n.steps.some((s) => s.state === "FAILED" || s.state === "CANCELLED")) ??
    nodes.find((n) => n.state === "FAILED" && n.steps.length > 0)
  );
}

/** The node that recorded the step at `position`, or undefined when no node holds it. */
export function nodeAtPosition(nodes: readonly RunGraphFlatNode[], position: number): RunGraphFlatNode | undefined {
  return nodes.find((n) => n.steps.some((s) => s.position === position));
}

type FlowNode = Pick<RunGraphFlatNode, "id" | "kind" | "state" | "parentId" | "track" | "tracks"> &
  Partial<Pick<RunGraphFlatNode, "checkpointed" | "replayed" | "opaque" | "canResume">>;

/**
 * The Graph component's nodes and edges for a chain's flat node list (runGraph.allNodes, or a
 * train's chain): each node toned by its state, each track's first step reached from its routing
 * step by an edge named after the track (dashed when the run did not take it), and every track's
 * last step, or the routing step itself for an empty track, joined to the step after it.
 */
export function runGraphFlow(all: readonly FlowNode[]): { nodes: GraphNodeSpec[]; edges: GraphEdgeSpec[] } {
  const byId = new Map(all.map((n) => [n.id, n]));
  // Each level's nodes in list order: the top level under "", a track under "parent\u0000track".
  const levels = new Map<string, FlowNode[]>();
  const levelOf = (parent: string | null, track: string | null) =>
    parent == null || track == null || !byId.has(parent) ? "" : `${parent}\u0000${track}`;
  for (const n of all) {
    const key = levelOf(n.parentId, n.track);
    const list = levels.get(key);
    if (list) list.push(n);
    else levels.set(key, [n]);
  }
  const level = (parent: string, track: string) => levels.get(`${parent}\u0000${track}`) ?? [];

  const nodes: GraphNodeSpec[] = all.map((n) => {
    const badges: GraphBadge[] = [];
    if (n.checkpointed) badges.push({ label: "checkpoint", tone: "info", title: "A checkpoint the run can resume from is stored here" });
    if (n.replayed) badges.push({ label: "replayed", tone: "idle", title: "An answer it acted on came from an earlier run" });
    if (n.opaque) badges.push({ label: "opaque", tone: "idle", title: "Which junction runs here is decided only at run time" });
    return {
      id: n.id,
      label: nodeLabel(n.id),
      detail: `${stepKindLabel(n.kind)} · ${NODE_STATE_LABEL[n.state]}`,
      tone: NODE_STATE_TONE[n.state],
      badges,
    };
  });

  const edges: GraphEdgeSpec[] = [];
  let seq = 0;
  const edge = (source: string, target: string, label?: string, dashed?: boolean, tone?: GraphTone) =>
    edges.push({ id: `e${seq++}`, source, target, label, dashed, tone });

  // Where a node's flow leaves it: the node itself, or for a branching node the ends of its
  // tracks, an empty track leaving from the node under that track's name.
  type Exit = { id: string; label?: string; dashed?: boolean };
  const exits = (n: FlowNode): Exit[] => {
    if (n.tracks.length === 0) return [{ id: n.id }];
    return n.tracks.flatMap((t): Exit[] => {
      const inner = level(n.id, t.name);
      const dashed = n.kind !== "PARALLEL" && !t.taken;
      if (inner.length === 0) return [{ id: n.id, label: t.name, dashed }];
      return exits(inner[inner.length - 1]).map((e) => ({ ...e, dashed: e.dashed || dashed }));
    });
  };

  const walk = (list: FlowNode[]) => {
    list.forEach((n, i) => {
      for (const t of n.tracks) {
        const inner = level(n.id, t.name);
        if (inner.length === 0) continue;
        const taken = n.kind === "PARALLEL" || t.taken;
        edge(n.id, inner[0].id, t.name, !taken, t.taken && n.kind !== "PARALLEL" ? "accent" : undefined);
        walk(inner);
      }
      const next = list[i + 1];
      if (!next) return;
      for (const e of exits(n)) edge(e.id, next.id, e.label, e.dashed);
    });
  };
  walk(levels.get("") ?? []);

  return { nodes, edges };
}
