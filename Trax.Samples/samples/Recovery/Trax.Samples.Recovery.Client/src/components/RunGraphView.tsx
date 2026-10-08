import type { GraphNode, NodeState, RunGraph } from "../types";

const STATE_TEXT: Record<NodeState, string> = {
  NOT_REACHED: "not reached",
  IN_PROGRESS: "running",
  COMPLETED: "completed",
  FAILED: "failed",
  CANCELLED: "cancelled",
  SKIPPED: "skipped",
  NOT_RECORDED: "not recorded",
  WITHHELD: "withheld",
};

// Steps that record nothing when they run, so the graph has nothing to say about them.
const UNRECORDED = new Set(["RESOLVE", "EXTRACT", "SEED"]);

/** A node's name: its junction, the question a Decide asks, or the routing step's own name. */
function nameOf(node: GraphNode): string {
  const bare = (node.id.split("/").pop() ?? node.id).replace(/#\d+$/, "");
  if (node.kind === "DECIDE") return `${bare.match(/<(\w+)>+$/)?.[1] ?? bare}?`;
  return node.junction ?? bare;
}

/**
 * One attempt drawn on its train's declared chain, from operations.runGraph: each step in the order the
 * chain declares it, a routing step's tracks side by side with the one taken marked, and a Parallel step's
 * branches side by side, since they ran at the same time.
 */
export function RunGraphView({ graph }: { graph: RunGraph }) {
  return (
    <div className="run-graph" role="group" aria-label="Run graph">
      <Nodes nodes={graph.nodes} />
    </div>
  );
}

function Nodes({ nodes }: { nodes: GraphNode[] }) {
  return (
    <ol className="graph-row">
      {nodes
        .filter((node) => !UNRECORDED.has(node.kind))
        .map((node) => (
          <li key={node.id}>
            <Node node={node} />
          </li>
        ))}
    </ol>
  );
}

function Node({ node }: { node: GraphNode }) {
  const state = STATE_TEXT[node.state];
  const chip = (
    <span className={`graph-node ${node.state.toLowerCase()}`} title={`${node.id}: ${state}`}>
      {nameOf(node)}
      {node.replayed && <span className="graph-replayed">replayed</span>}
      <span className="sr-only">, {state}</span>
    </span>
  );
  if (node.tracks.length === 0) return chip;

  const parallel = node.kind === "PARALLEL";
  return (
    <div className={`graph-split ${parallel ? "parallel" : "route"}`}>
      {chip}
      <ul className="graph-tracks" aria-label={parallel ? "Branches, run side by side" : "Tracks"}>
        {node.tracks.map((track) => (
          <li key={track.name} className={`graph-track ${parallel || track.taken ? "taken" : "passed"}`}>
            <span className="graph-track-name">
              {parallel ? `${track.name} branch` : track.name}
              {!parallel && track.taken ? " (taken)" : ""}
            </span>
            <Nodes nodes={track.nodes ?? []} />
          </li>
        ))}
      </ul>
    </div>
  );
}
