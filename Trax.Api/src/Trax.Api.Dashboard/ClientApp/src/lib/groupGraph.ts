import type { GraphEdgeSpec, GraphNodeSpec } from "./graphLayout";
import type { ManifestGroupDependencyGraph } from "../types";

/** A manifest-group dependency graph as the shared Graph component draws it: parent → dependent. */
export function groupGraph(graph: ManifestGroupDependencyGraph | undefined): {
  nodes: GraphNodeSpec[];
  edges: GraphEdgeSpec[];
} {
  return {
    nodes: (graph?.nodes ?? []).map((n) => ({
      id: String(n.id),
      label: n.name,
      tone: n.isHighlighted ? "accent" : "idle",
    })),
    edges: (graph?.edges ?? []).map((e) => ({
      id: `${e.fromId}->${e.toId}`,
      source: String(e.fromId),
      target: String(e.toId),
    })),
  };
}
