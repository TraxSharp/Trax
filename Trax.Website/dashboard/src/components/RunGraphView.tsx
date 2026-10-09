import { GitFork, Play, Route } from "lucide-react";
import { stepFailure } from "../lib/junctionSteps";
import type { ChainStepKind, JunctionRunState, RunGraph, RunGraphNode, RunNodeState } from "../types";

const NODE_DOT: Partial<Record<RunNodeState, string>> = {
  IN_PROGRESS: "bg-info animate-pulse",
  COMPLETED: "bg-ok",
  FAILED: "bg-danger",
  CANCELLED: "bg-warn",
  RESTORED: "bg-accent",
  // Started and never recorded an end in a run that has ended: neither running nor done.
  INTERRUPTED: "border border-dashed border-warn",
};

const STEP_DOT: Record<JunctionRunState, string> = {
  IN_PROGRESS: "bg-info animate-pulse",
  COMPLETED: "bg-ok",
  FAILED: "bg-danger",
  CANCELLED: "bg-warn",
};

const STATE_LABEL: Record<RunNodeState, string> = {
  NOT_REACHED: "not reached",
  IN_PROGRESS: "running",
  COMPLETED: "completed",
  FAILED: "failed",
  CANCELLED: "cancelled",
  SKIPPED: "skipped",
  NOT_RECORDED: "not recorded",
  WITHHELD: "withheld",
  RESTORED: "restored",
  INTERRUPTED: "interrupted",
};

function kindLabel(kind: ChainStepKind): string {
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

// The last segment of the id names the step within its track, as "Ship#0".
function nameOf(node: RunGraphNode): string {
  const slash = node.id.lastIndexOf("/");
  return slash < 0 ? node.id : node.id.slice(slash + 1);
}

const DIM = new Set<RunNodeState>(["SKIPPED", "NOT_REACHED"]);

export interface RunGraphViewProps {
  graph: RunGraph;
  // Called with a node's id when the operator asks to resume there. Without it no node offers
  // "Resume from here".
  onResume?: (nodeId: string) => void;
  // The node whose resume is in flight.
  resumingNode?: string | null;
  // True while any re-queue or resume of the page is in flight.
  resumeDisabled?: boolean;
}

/**
 * One run drawn on its train's declared chain, as operations.runGraph places its recorded steps:
 * each node with where the run left it, the track each routing step took, a Parallel step's
 * branches side by side, and the steps that match no node. A node shows that a checkpoint is
 * stored there and offers "Resume from here" where resumeExecution can resume; it never shows what
 * a checkpoint holds. Mirrors the Blazor RunGraphView.
 */
export function RunGraphView({ graph, onResume, resumingNode, resumeDisabled }: RunGraphViewProps) {
  return (
    <section aria-label="Run graph" className="bg-surface rounded-lg border border-line p-5 mb-6">
      <h2 className="text-sm font-semibold text-fg mb-3">Run graph</h2>
      {!graph.hasGraph ? (
        <p className="text-sm text-muted">
          This host has no declared graph for this train: it is not registered here, or its chain
          cannot be read outside a request. The steps the run recorded are in the timeline below.
        </p>
      ) : (
        <>
          {graph.moreSteps && (
            <p className="text-sm text-warn-fg mb-2">
              Only the run's first steps are placed; a later node can show as not reached when it ran.
            </p>
          )}
          <ol aria-label="Declared steps of the train" className="space-y-1">
            {graph.nodes.map((node) => (
              <Node
                key={node.id}
                node={node}
                onResume={onResume}
                resumingNode={resumingNode}
                resumeDisabled={resumeDisabled}
              />
            ))}
          </ol>
          {graph.unmatchedSteps.length > 0 && (
            <section aria-label="Steps that match no node" className="mt-4">
              <h3 className="text-xs font-semibold text-fg mb-1">Steps not on the graph</h3>
              <p className="text-xs text-muted mb-1">
                Recorded without a node id (before node ids were recorded, or after a route whose
                answer is withheld), or for a step the chain no longer declares.
              </p>
              <ul className="space-y-1">
                {graph.unmatchedSteps.map((step) => (
                  <li
                    key={step.position}
                    data-position={step.position}
                    className="flex items-center gap-2 text-sm"
                  >
                    <span aria-hidden className={`size-2 rounded-full ${STEP_DOT[step.state]}`} />
                    <span className="text-xs text-muted">#{step.position}</span>
                    <span className={step.nameWithheld ? "italic text-muted" : "text-fg"}>
                      {step.nameWithheld ? "withheld" : step.name}
                    </span>
                    <span className="text-xs text-muted">{step.state.toLowerCase().replace("_", " ")}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </>
      )}
    </section>
  );
}

function Node({
  node,
  onResume,
  resumingNode,
  resumeDisabled,
}: { node: RunGraphNode } & Omit<RunGraphViewProps, "graph">) {
  const failed = node.steps.find((s) => s.state === "FAILED" || s.state === "CANCELLED");
  const tracks = node.tracks ?? [];
  const resuming = resumingNode === node.id;
  return (
    <li
      data-node-id={node.id}
      data-kind={node.kind}
      data-state={node.state}
      data-can-resume={node.canResume ? "true" : "false"}
      className={DIM.has(node.state) ? "opacity-60" : undefined}
    >
      <div className="flex flex-wrap items-center gap-2 text-sm" title={node.id}>
        <span
          aria-hidden
          className={`size-2 rounded-full shrink-0 ${NODE_DOT[node.state] ?? "border border-line-strong"}`}
        />
        <span className="text-xs text-muted">{kindLabel(node.kind)}</span>
        <span className="font-medium text-fg">{nameOf(node)}</span>
        <span className="text-xs text-muted">{STATE_LABEL[node.state]}</span>
        {node.opaque && (
          <span
            title="Which junction runs here is decided only at run time"
            className="text-xs px-1.5 py-0.5 rounded border border-line-strong text-muted"
          >
            opaque
          </span>
        )}
        {node.replayed && (
          <span className="text-xs px-1.5 py-0.5 rounded border border-line-strong text-fg-2">replayed</span>
        )}
        {node.checkpointed && (
          // That a checkpoint is stored here, never what it holds.
          <span
            title="A checkpoint the run can resume from is stored here"
            className="text-xs px-1.5 py-0.5 rounded border border-info-line text-info-fg"
          >
            checkpoint
          </span>
        )}
        {node.canResume && onResume && (
          <button
            onClick={() => onResume(node.id)}
            disabled={resumeDisabled}
            title="Queue a run that skips to this step, on what the checkpoint before it restores."
            className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-md text-accent-fg hover:bg-accent-soft disabled:opacity-50"
          >
            <Play className="size-3" aria-hidden />
            {resuming ? "Resuming…" : "Resume from here"}
          </button>
        )}
        {failed && <span className="text-xs text-danger-fg">{stepFailure(failed)}</span>}
      </div>
      {node.kind === "PARALLEL" && tracks.length > 0 ? (
        // Every branch runs, so the branches sit side by side as lanes rather than as alternatives
        // stacked under the step.
        <ul
          aria-label={`Branches of ${nameOf(node)}, run side by side`}
          className="ml-4 mt-1 grid gap-2 sm:grid-cols-2"
        >
          {tracks.map((branch) => (
            <li
              key={branch.name}
              data-branch={branch.name}
              data-taken={branch.taken ? "true" : "false"}
              className={`rounded-md border border-line p-2 ${branch.taken ? "" : "opacity-60"}`}
            >
              <div className="flex items-center gap-1 text-xs text-muted mb-1">
                <GitFork className="size-3" aria-hidden />
                branch <span className="text-fg">{branch.name}</span>
              </div>
              {branch.nodes.length > 0 && (
                <ol aria-label={`Steps of branch ${branch.name}`} className="space-y-1">
                  {branch.nodes.map((inner) => (
                    <Node
                      key={inner.id}
                      node={inner}
                      onResume={onResume}
                      resumingNode={resumingNode}
                      resumeDisabled={resumeDisabled}
                    />
                  ))}
                </ol>
              )}
            </li>
          ))}
        </ul>
      ) : (
        tracks.length > 0 && (
          <ul aria-label={`Tracks of ${nameOf(node)}`} className="ml-4 mt-1 space-y-1 border-l border-line pl-3">
            {tracks.map((track) => (
              <li key={track.name} data-track={track.name} data-taken={track.taken ? "true" : "false"}>
                <div
                  className={`flex items-center gap-1 text-xs ${track.taken ? "text-fg" : "text-muted"}`}
                  title={track.description ?? undefined}
                >
                  <Route className="size-3" aria-hidden />
                  {track.name}
                  {track.isFallback && <span className="text-muted">fallback</span>}
                  {track.taken && <span className="text-ok-fg font-medium">taken</span>}
                </div>
                {track.nodes.length > 0 && (
                  <ol className="space-y-1 mt-1">
                    {track.nodes.map((inner) => (
                      <Node
                        key={inner.id}
                        node={inner}
                        onResume={onResume}
                        resumingNode={resumingNode}
                        resumeDisabled={resumeDisabled}
                      />
                    ))}
                  </ol>
                )}
              </li>
            ))}
          </ul>
        )
      )}
    </li>
  );
}
