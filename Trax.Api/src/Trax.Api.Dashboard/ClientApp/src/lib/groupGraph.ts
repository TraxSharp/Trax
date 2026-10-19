import type { GraphEdgeSpec, GraphNodeSpec, GraphTone } from "./graphLayout";
import type { GraphLegendEntry } from "../components/Graph";
import type { ManifestGroupDependencyGraph, ManifestGroupStats } from "../types";

/** What a group's node is toned by: a run in progress, and whether its latest ended run failed. */
export type GroupStatus = Pick<ManifestGroupStats, "groupId" | "inProgress" | "lastEndedRunFailed">;

/** The tones a group's node takes, worst first, as the graph's legend lists them. */
export const GROUP_GRAPH_LEGEND: GraphLegendEntry[] = [
  { label: "Last run failed", tone: "danger" },
  { label: "Running", tone: "info" },
  { label: "Enabled", tone: "ok" },
  { label: "Disabled", tone: "idle" },
];

/** The words and tone of one group's node: failing first, then running, then enabled or disabled. */
export function groupNodeState(
  isEnabled: boolean | undefined,
  status: GroupStatus | undefined,
): { tone: GraphTone; detail?: string } {
  const words: string[] = [];
  if (isEnabled === false) words.push("Disabled");
  if (status?.lastEndedRunFailed) words.push("Last run failed");
  if (status && status.inProgress > 0) words.push(`${status.inProgress} running`);
  const tone: GraphTone = status?.lastEndedRunFailed
    ? "danger"
    : status && status.inProgress > 0
      ? "info"
      : isEnabled === false
        ? "idle"
        : isEnabled === true
          ? "ok"
          : "idle";
  if (words.length === 0 && isEnabled === true) words.push("Enabled");
  return { tone, detail: words.length ? words.join(" · ") : undefined };
}

/**
 * A manifest-group dependency graph as the shared Graph component draws it: parent → dependent,
 * each node toned by whether its group is enabled, running or failing. A graph answered without
 * isEnabled (a recording made before it existed) and without statuses draws its nodes plain.
 */
export function groupGraph(
  graph: ManifestGroupDependencyGraph | undefined,
  statuses: GroupStatus[] = [],
): {
  nodes: GraphNodeSpec[];
  edges: GraphEdgeSpec[];
} {
  const byId = new Map(statuses.map((s) => [s.groupId, s]));
  return {
    nodes: (graph?.nodes ?? []).map((n) => ({
      id: String(n.id),
      label: n.name,
      ...groupNodeState(n.isEnabled, byId.get(n.id)),
    })),
    edges: (graph?.edges ?? []).map((e) => ({
      id: `${e.fromId}->${e.toId}`,
      source: String(e.fromId),
      target: String(e.toId),
    })),
  };
}
