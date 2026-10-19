import type { GraphEdgeSpec, GraphNodeSpec } from "./graphLayout";
import type { ChainStepKind, RunNodeState, TrainChainNode } from "../types";

// A train's declared chain (operations.trains { chain }) as the train page draws it: the chain on
// the shared Graph, each step's failures over a range, and the rows of its grid of recent runs.

/** The recent runs the train page's grid draws, each one run-graph read: at most this many per load. */
export const GRID_RUNS = 20;

/** A step's name in its track: the last segment of its id without its index ("Route#3/Short/Trim#0" -> "Trim"). */
export function stepName(id: string): string {
  const last = id.slice(id.lastIndexOf("/") + 1);
  return last.replace(/#\d+$/, "") || last;
}

/** How a chain primitive reads on a node. */
export function stepKindLabel(kind: ChainStepKind): string {
  switch (kind) {
    case "CHAIN":
      return "Junction";
    case "I_CHAIN":
      return "Junction (interface)";
    case "SHORT_CIRCUIT":
      return "Short circuit";
    default:
      return kind.charAt(0) + kind.slice(1).toLowerCase();
  }
}

// The nodes on one track of `parentId` (the top level when null), in the list's order. A node whose
// parent the list does not carry sits at the top level, as lib/runGraphTree places it.
function onTrack(nodes: readonly TrainChainNode[], ids: ReadonlySet<string>, parentId: string | null, track: string | null) {
  return nodes.filter((n) =>
    parentId == null ? n.parentId == null || !ids.has(n.parentId) || n.track == null : n.parentId === parentId && n.track === track,
  );
}

// The tracks of a routing or Parallel step: the ones it declares, then any its children name.
function tracksOf(nodes: readonly TrainChainNode[], node: TrainChainNode): string[] {
  const names = node.tracks.map((t) => t.name);
  for (const n of nodes) if (n.parentId === node.id && n.track != null && !names.includes(n.track)) names.push(n.track);
  return names;
}

/**
 * The chain as the Graph draws it, left to right: each step after the one before it, a routing or
 * Parallel step fanning out to the first step of each track (the edge names the track) and every
 * track's last step joining the step after it. A step with failures in the range carries a badge.
 */
export function chainGraph(
  nodes: readonly TrainChainNode[],
  failures: ReadonlyMap<string, number> = new Map(),
  failuresTitle = "Failed runs",
): { nodes: GraphNodeSpec[]; edges: GraphEdgeSpec[] } {
  const ids = new Set(nodes.map((n) => n.id));
  const edges = new Map<string, GraphEdgeSpec>();
  const link = (source: string, target: string, label?: string) => {
    const id = `${source}->${target}`;
    if (!edges.has(id)) edges.set(id, { id, source, target, label });
  };
  type From = { id: string; label?: string };
  const walk = (list: TrainChainNode[], from: From[]): From[] => {
    let preds = from;
    for (const node of list) {
      for (const p of preds) link(p.id, node.id, p.label);
      const tracks = tracksOf(nodes, node);
      if (tracks.length === 0) {
        preds = [{ id: node.id }];
        continue;
      }
      const exits: From[] = [];
      for (const track of tracks) {
        const steps = onTrack(nodes, ids, node.id, track);
        // A track with no steps passes straight through its step.
        if (steps.length === 0) exits.push({ id: node.id });
        else exits.push(...walk(steps, [{ id: node.id, label: track }]));
      }
      preds = exits.filter((e, i) => exits.findIndex((o) => o.id === e.id) === i);
    }
    return preds;
  };
  walk(onTrack(nodes, ids, null, null), []);

  return {
    nodes: nodes.map((n) => {
      const failed = failures.get(n.id) ?? 0;
      return {
        id: n.id,
        label: stepName(n.id),
        detail: n.opaque ? `${stepKindLabel(n.kind)}, decided at run time` : stepKindLabel(n.kind),
        tone: failed > 0 ? "danger" : "idle",
        badges: failed > 0 ? [{ label: String(failed), tone: "danger", title: failuresTitle }] : undefined,
      };
    }),
    edges: [...edges.values()],
  };
}

/**
 * Where the failed runs of a range stopped, by step. A run's failure names the junction it stopped
 * in (a Parallel branch's as "branch:junction"); it is counted on the one step that runs that
 * junction. A failure outside any step, or at a junction several steps run, is counted elsewhere.
 */
export function failuresByStep(
  nodes: readonly TrainChainNode[],
  failedRuns: readonly { failureJunction: string | null }[],
): { byStep: Map<string, number>; elsewhere: number } {
  const byStep = new Map<string, number>();
  let elsewhere = 0;
  for (const run of failedRuns) {
    const named = run.failureJunction?.slice(run.failureJunction.lastIndexOf(":") + 1);
    const at = named ? nodes.filter((n) => n.junction === named || stepName(n.id) === named) : [];
    if (at.length === 1) byStep.set(at[0].id, (byStep.get(at[0].id) ?? 0) + 1);
    else elsewhere += 1;
  }
  return { byStep, elsewhere };
}

/** One row of the grid of recent runs: a step, how deep in its tracks it sits, and the track. */
export interface StepRow {
  id: string;
  label: string;
  depth: number;
  track: string | null;
}

/** The grid's rows: every step at any depth, in declared order (the order the chain lists them). */
export function stepRows(nodes: readonly TrainChainNode[]): StepRow[] {
  return nodes.map((n) => ({ id: n.id, label: stepName(n.id), depth: n.depth, track: n.track }));
}

/** How a step's state in one run reads in the grid. */
export const STEP_STATE_LABEL: Record<RunNodeState, string> = {
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
