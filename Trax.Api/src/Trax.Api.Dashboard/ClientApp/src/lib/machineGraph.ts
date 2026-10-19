import { parseDuration, UNIT_LABEL } from "./duration";
import { shortName } from "./format";
import type { GraphBadge, GraphEdgeSpec, GraphNodeSpec, GraphTone } from "./graphLayout";
import type { MachineDefinition, MachineDefinitionTransition } from "../types";

// A machine's definition (operations.machines) as the shared Graph draws it (api/0044): one node
// per state, one edge per transition. A command's edge is labelled with the command; a timer's is
// dashed and labelled with its delay; an outcome's (a run the state invoked ended) is toned by how
// the run ended. The machine page badges each state with its instance and stuck counts; the
// instance page lights the state the instance is in.

/** How many instances are in a state, and how many of them are stuck. */
export interface StateCount {
  count: number;
  stuck: number;
}

export interface MachineGraphOptions {
  /** Per-state counts: each state is badged with them. */
  counts?: ReadonlyMap<string, StateCount>;
  /** The state one instance is in: lit, with the edges that leave it. */
  current?: string;
}

/** "PT1H" -> "1 hour", "PT90S" -> "90 seconds"; anything else (a context field) as it came. */
export function delayWords(after: string | null | undefined): string {
  if (!after) return "a delay";
  const { value, unit } = parseDuration(after);
  if (!value) return after;
  const label = UNIT_LABEL[unit];
  return `${value} ${value === "1" ? label.slice(0, -1) : label}`;
}

/** The ending an outcome trigger names: "Building.done" -> "done". */
export function outcomeOf(t: Pick<MachineDefinitionTransition, "command">): string {
  const dot = t.command.lastIndexOf(".");
  return dot >= 0 ? t.command.slice(dot + 1) : t.command;
}

const OUTCOME_TONE: Record<string, GraphTone> = { done: "ok", failed: "danger", cancelled: "warn" };

/** What an edge says: the command, the timer's delay, or how the invoked run ended. */
export function transitionLabel(t: MachineDefinitionTransition): string {
  switch (t.kind) {
    case "TIMER":
      return `after ${delayWords(t.after)}`;
    case "OUTCOME":
      return `run ${outcomeOf(t)}`;
    default:
      return t.command;
  }
}

/** Sums counts by state across owner kinds. */
export function countsByState(
  rows: readonly { state: string; count: number; stuck?: number }[],
): Map<string, StateCount> {
  const out = new Map<string, StateCount>();
  for (const r of rows) {
    const c = out.get(r.state) ?? { count: 0, stuck: 0 };
    c.count += r.count;
    c.stuck += r.stuck ?? 0;
    out.set(r.state, c);
  }
  return out;
}

export function machineGraph(
  machine: MachineDefinition,
  { counts, current }: MachineGraphOptions = {},
): { nodes: GraphNodeSpec[]; edges: GraphEdgeSpec[] } {
  const nodes = machine.states.map<GraphNodeSpec>((s) => {
    const c = counts?.get(s.name) ?? { count: 0, stuck: 0 };
    const badges: GraphBadge[] = [];
    if (counts) {
      badges.push({ label: c.count.toLocaleString("en-US"), tone: c.count > 0 ? "info" : "idle", title: "Instances" });
      if (c.stuck > 0) badges.push({ label: c.stuck.toLocaleString("en-US"), tone: "danger", title: "Stuck" });
    }
    const tone: GraphTone =
      current != null
        ? s.name === current
          ? "accent"
          : "idle"
        : c.stuck > 0
          ? "danger"
          : c.count > 0
            ? "info"
            : "idle";
    return {
      id: s.name,
      label: s.name,
      detail: s.invokesTrain
        ? `runs ${shortName(s.invokesTrain)}`
        : s.spawnsMachine
          ? `starts ${s.spawnsMachine}`
          : undefined,
      tone,
      badges: badges.length > 0 ? badges : undefined,
      initial: s.isInitial,
      final: s.isFinal,
      current: current != null && s.name === current,
    };
  });
  const edges = machine.transitions.map<GraphEdgeSpec>((t, i) => ({
    id: `${i}:${t.from}:${t.command}:${t.to}`,
    source: t.from,
    target: t.to,
    label: transitionLabel(t),
    dashed: t.kind === "TIMER",
    tone:
      t.kind === "OUTCOME"
        ? (OUTCOME_TONE[outcomeOf(t)] ?? "idle")
        : current != null && t.from === current
          ? "accent"
          : "idle",
  }));
  return { nodes, edges };
}
