import { useMemo } from "react";
import { Play, X } from "lucide-react";
import { Graph } from "./Graph";
import { formatMs, shortName } from "../lib/format";
import { formatConfidence, kindLabel, stepFailure } from "../lib/junctionSteps";
import {
  NODE_STATE_LABEL,
  RUN_GRAPH_LEGEND,
  nodeLabel,
  runGraphFlow,
  stepKindLabel,
} from "../lib/runGraphFlow";
import type { JunctionRunState, RunGraph, RunGraphFlatNode, RunGraphStep } from "../types";

const RUN_GRAPH_DOCS = "https://traxsharp.net/docs/sdk-reference/graphql-api/queries/executions#rungraph";

const STEP_DOT: Record<JunctionRunState, string> = {
  IN_PROGRESS: "bg-info animate-pulse",
  COMPLETED: "bg-ok",
  FAILED: "bg-danger",
  CANCELLED: "bg-warn",
};

export interface RunGraphPanelProps {
  graph: RunGraph;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  /** Called with a node's id to resume there. Without it no node offers "Resume from here". */
  onResume?: (nodeId: string) => void;
  /** The node whose resume is in flight. */
  resumingNode?: string | null;
  /** True while any requeue or resume of the page is in flight. */
  resumeDisabled?: boolean;
}

/**
 * One run drawn on its train's declared chain (operations.runGraph) with the shared Graph
 * component: each node coloured by where the run left it, a routing step's tracks and a Parallel
 * step's branches as branches. Selecting a node opens its panel: what it takes and gives, its
 * failure, its attempts and the decisions it recorded, and "Resume from here" where the run can
 * resume. A node says that a checkpoint is stored there, never what it holds.
 */
export function RunGraphPanel({ graph, selectedId, onSelect, onResume, resumingNode, resumeDisabled }: RunGraphPanelProps) {
  const flow = useMemo(() => runGraphFlow(graph.allNodes), [graph.allNodes]);
  const selected = graph.allNodes.find((n) => n.id === selectedId) ?? null;

  return (
    <section aria-label="Run graph" className="bg-surface rounded-lg border border-line p-5 mb-6">
      <h2 className="text-sm font-semibold text-fg mb-3">Run graph</h2>
      {!graph.hasGraph ? (
        <p className="text-sm text-muted">
          This host has no declared graph for this train: it is not registered here, or its chain
          cannot be read outside a request. The steps the run recorded are in the timeline below.{" "}
          <a className="text-accent-fg hover:underline" href={RUN_GRAPH_DOCS} target="_blank" rel="noreferrer">
            How a run is drawn
          </a>
        </p>
      ) : (
        <>
          {graph.chainChanged && (
            <p role="note" className="text-sm rounded-md border border-warn-line bg-warn-soft text-warn-fg px-3 py-2 mb-3">
              This train's chain has changed since this run started. The graph is today's chain, so a
              step can sit where the run did not run it.{" "}
              <a className="underline" href={RUN_GRAPH_DOCS} target="_blank" rel="noreferrer">
                Why
              </a>
            </p>
          )}
          {graph.moreSteps && (
            <p className="text-sm text-warn-fg mb-2">
              Only the run's first steps are placed; a later node can show as not reached when it ran.
            </p>
          )}
          <div className={`grid gap-4 ${selected ? "lg:grid-cols-[minmax(0,1fr)_20rem]" : ""}`}>
            <div className="min-w-0">
              <Graph
                nodes={flow.nodes}
                edges={flow.edges}
                ariaLabel="Steps of the train"
                selectedId={selectedId ?? undefined}
                onSelect={(id) => onSelect(id === selectedId ? null : id)}
                legend={RUN_GRAPH_LEGEND}
              />
            </div>
            {selected && (
              <NodePanel
                node={selected}
                onClose={() => onSelect(null)}
                onResume={onResume}
                resuming={resumingNode === selected.id}
                resumeDisabled={resumeDisabled}
              />
            )}
          </div>
          {graph.unmatchedSteps.length > 0 && (
            <section aria-label="Steps that match no node" className="mt-4">
              <h3 className="text-xs font-semibold text-fg mb-1">Steps not on the graph</h3>
              <p className="text-xs text-muted mb-1">
                Recorded without a node id (before node ids were recorded, or after a route whose
                answer is withheld), or for a step the chain no longer declares.
              </p>
              <ul className="space-y-1">
                {graph.unmatchedSteps.map((step) => (
                  <li key={step.position} data-position={step.position} className="flex items-center gap-2 text-sm">
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

function NodePanel({
  node,
  onClose,
  onResume,
  resuming,
  resumeDisabled,
}: {
  node: RunGraphFlatNode;
  onClose: () => void;
  onResume?: (nodeId: string) => void;
  resuming: boolean;
  resumeDisabled?: boolean;
}) {
  const label = nodeLabel(node.id);
  const failures = node.steps.filter((s) => s.state === "FAILED" || s.state === "CANCELLED");
  const decisions = node.steps.filter((s) => s.kind !== "JUNCTION");
  const attempt = node.steps.find((s) => s.attempt != null)?.attempt ?? null;
  return (
    <aside
      aria-label={`Step ${label}`}
      data-node-id={node.id}
      data-state={node.state}
      data-kind={node.kind}
      data-can-resume={node.canResume ? "true" : "false"}
      className="rounded-md border border-line bg-canvas p-4 text-sm"
    >
      <div className="flex items-start gap-2 mb-3">
        <div className="min-w-0">
          <h3 className="font-semibold text-fg truncate" title={node.id}>
            {label}
          </h3>
          <p className="text-xs text-muted">
            {stepKindLabel(node.kind)} · {NODE_STATE_LABEL[node.state]}
          </p>
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label="Close step"
          className="ml-auto rounded p-1 text-muted hover:bg-hover hover:text-fg"
        >
          <X className="size-4" aria-hidden />
        </button>
      </div>

      <dl className="space-y-1 mb-3">
        <Row label="Node">
          <span className="font-mono text-xs break-all">{node.id}</span>
        </Row>
        {node.track != null && <Row label="On track">{node.track}</Row>}
        {node.junction && <Row label="Runs">{shortName(node.junction)}</Row>}
        <Row label="Takes">{node.in ? shortName(node.in) : "—"}</Row>
        <Row label="Gives">{node.out ? shortName(node.out) : "—"}</Row>
        {attempt != null && <Row label="Attempt">{attempt}</Row>}
      </dl>
      <p className="text-xs text-muted mb-3">
        A step's own input and output are not recorded; the run's are under Input and output.
      </p>

      {(node.checkpointed || node.replayed || node.opaque) && (
        <ul className="flex flex-wrap gap-1 mb-3">
          {node.checkpointed && (
            <li className="text-xs px-1.5 py-0.5 rounded border border-info-line text-info-fg">checkpoint stored</li>
          )}
          {node.replayed && <li className="text-xs px-1.5 py-0.5 rounded border border-line-strong text-fg-2">replayed</li>}
          {node.opaque && (
            <li className="text-xs px-1.5 py-0.5 rounded border border-line-strong text-muted">decided at run time</li>
          )}
        </ul>
      )}

      {failures.length > 0 && (
        <div className="mb-3">
          <h4 className="text-xs font-semibold text-danger-fg mb-1">Error</h4>
          {failures.map((s) => (
            <p key={s.position} className="text-sm text-danger-fg break-words">
              {stepFailure(s)}
            </p>
          ))}
        </div>
      )}

      <div className="mb-3">
        <h4 className="text-xs font-semibold text-fg mb-1">Steps recorded</h4>
        {node.steps.length === 0 ? (
          <p className="text-xs text-muted">None.</p>
        ) : (
          <ol className="space-y-1">
            {node.steps.map((s) => (
              <StepLine key={s.position} step={s} />
            ))}
          </ol>
        )}
      </div>

      {decisions.length > 0 && (
        <div className="mb-3">
          <h4 className="text-xs font-semibold text-fg mb-1">Decisions</h4>
          <ul className="space-y-1">
            {decisions.map((s) => (
              <li key={s.position} className="text-xs text-fg-2">
                <DecisionLine step={s} />
              </li>
            ))}
          </ul>
        </div>
      )}

      {node.canResume && onResume && (
        <button
          type="button"
          onClick={() => onResume(node.id)}
          disabled={resumeDisabled}
          title="Queue a run that skips to this step, on what the checkpoint before it restores."
          className="inline-flex items-center gap-1 text-sm px-3 py-1 rounded-md border border-accent-line text-accent-fg hover:bg-accent-soft disabled:opacity-50"
        >
          <Play className="size-3.5" aria-hidden />
          {resuming ? "Resuming…" : "Resume from here"}
        </button>
      )}
    </aside>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted shrink-0">{label}</dt>
      <dd className="text-fg text-right min-w-0">{children}</dd>
    </div>
  );
}

function StepLine({ step }: { step: RunGraphStep }) {
  return (
    <li className="flex items-center gap-2 text-xs">
      <span aria-hidden className={`size-2 rounded-full shrink-0 ${STEP_DOT[step.state]}`} />
      <span className="text-muted">#{step.position}</span>
      <span className="text-fg truncate">
        {step.kind !== "JUNCTION" ? kindLabel(step.kind) : step.nameWithheld ? "withheld" : step.name}
      </span>
      <span className="text-muted ml-auto shrink-0">
        {step.durationMs != null ? formatMs(step.durationMs) : step.state === "IN_PROGRESS" ? "running" : "—"}
        {step.attempt != null ? ` · attempt ${step.attempt}` : ""}
      </span>
    </li>
  );
}

// A question or route the node recorded: its key and the answer, unless withheld.
function DecisionLine({ step }: { step: RunGraphStep }) {
  if (step.nameWithheld) return <span className="italic">withheld</span>;
  const hidden = step.answerWithheld;
  return (
    <>
      <span>{step.questionKey ?? step.name}</span>
      <span aria-hidden> → </span>
      {hidden ? (
        <span className="italic">withheld</span>
      ) : (
        <span className="font-medium text-fg">{step.answer ?? "no answer"}</span>
      )}
      {!hidden && step.confidence != null && <span> · {formatConfidence(step.confidence)} confidence</span>}
      {step.decider && <span> · {shortName(step.decider)}</span>}
      {step.replayed && <span> · replayed</span>}
    </>
  );
}
