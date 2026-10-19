import { createContext, useContext, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { EXECUTION_DETAIL, RUN_GRAPH, RUN_LINKS } from "../graphql/queries";
import { CANCEL_EXECUTION, REQUEUE_EXECUTION, RESUME_EXECUTION } from "../graphql/mutations";
import { StateBadge } from "../components/StateBadge";
import { ExecutionChildren } from "../components/ExecutionChildren";
import { JunctionTimeline } from "../components/JunctionTimeline";
import { RunGraphPanel } from "../components/RunGraphPanel";
import { DecisionsPanel } from "../components/DecisionsPanel";
import { LogsGrid } from "../components/LogsGrid";
import { TermHelp } from "../components/TermHelp";
import {
  BackLink,
  DetailLink,
  DetailPanel,
  ExceptionViewer,
  Fields,
  JsonPanel,
  Loading,
  NotFound,
} from "../components/detail";
import { formatMs, formatTime, prettyJson, shortName } from "../lib/format";
import { toast } from "../lib/toast";
import { useAnswers } from "../lib/answerable";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { useUrlState, useUrlStateOrNull } from "../lib/urlState";
import { machineInstancePath } from "../lib/machineInstances";
import { failedNode } from "../lib/runGraphFlow";
import type { ExecutionDetail, RunGraph, RunLinks } from "../types";
import { confirmAction } from "../lib/confirm";
import { offersDecisions, useTrainsAskingDecisions } from "../lib/decisions";

const ACTIVE_STATES = new Set(["PENDING", "IN_PROGRESS"]);
const TERMINAL_STATES = new Set(["COMPLETED", "FAILED", "CANCELLED"]);

const TABS = [
  ["timeline", "Timeline"],
  ["io", "Input and output"],
  ["logs", "Logs"],
  ["decisions", "Decisions"],
  ["related", "Related"],
] as const;
type Tab = (typeof TABS)[number][0];

interface ExecData {
  operations: { executionDetail: ExecutionDetail | null };
}

interface RunGraphData {
  operations: { runGraph: RunGraph | null };
}

interface RunLinksData {
  operations: { executionDetail: RunLinks | null };
}

/**
 * One run (api/0043): a header with its state, train, duration, what started it and the actions
 * on it; its chain drawn on the shared graph, coloured by this run, with a panel for the selected
 * step; and tabs for the timeline (synced to the graph's selection), input and output, logs,
 * decisions, and the runs and records around it.
 */
export function ExecutionDetailPage() {
  const id = Number(useParams().id);
  const [result, reexecute] = useQuery<ExecData>({
    query: EXECUTION_DETAIL,
    variables: { id },
  });
  // The run drawn on its train's declared graph, with the checkpoints it can resume from. Not asked
  // of a client that cannot answer it (the demo, until its recordings hold run graphs).
  const answersGraph = useAnswers("RunGraph");
  const [graphResult, reexecuteGraph] = useQuery<RunGraphData>({
    query: RUN_GRAPH,
    variables: { metadataId: id },
    pause: !answersGraph,
  });
  // What started the run and what it came from; the header names what it can without it.
  const answersLinks = useAnswers("RunLinks");
  const [linksResult] = useQuery<RunLinksData>({
    query: RUN_LINKS,
    variables: { id },
    pause: !answersLinks,
  });
  // An EXECUTION change names no run, so it refetches the run on any run's change. The graph is the
  // heavier read and changes only while this run is going, so it is read again only then, and once
  // more when the run's state moves (as it ends), so a finished run shows the graph it ended with.
  // reexecute ignores pause, so a client that cannot answer the graph is never sent one.
  const trainState = result.data?.operations?.executionDetail?.trainState;
  const active = trainState != null && ACTIVE_STATES.has(trainState);
  useRefetchOnChange("EXECUTION", () => {
    reexecute({ requestPolicy: "network-only" });
    if (answersGraph && active) reexecuteGraph({ requestPolicy: "network-only" });
  });
  const seenState = useRef(trainState);
  useEffect(() => {
    const before = seenState.current;
    seenState.current = trainState;
    if (answersGraph && before != null && trainState != null && before !== trainState)
      reexecuteGraph({ requestPolicy: "network-only" });
  }, [trainState, answersGraph, reexecuteGraph]);
  const [, cancelExecution] = useMutation(CANCEL_EXECUTION);
  const [, requeueExecution] = useMutation(REQUEUE_EXECUTION);
  const [, resumeExecution] = useMutation(RESUME_EXECUTION);
  const [busy, setBusy] = useState(false);
  // While a resume is in flight: the node a "Resume from here" names, or "" for the Resume button.
  const [resuming, setResuming] = useState<string | null>(null);
  const navigate = useNavigate();

  const [tab, setTab] = useUrlState<Tab>("tab", "timeline");
  // The selected step, in the URL so a link can open a run at it. Until one is chosen, a failed
  // run opens at the step that failed.
  const [step, setStep] = useUrlStateOrNull("step");
  const [cleared, setCleared] = useState(false);

  const e = result.data?.operations.executionDetail;
  const links = linksResult.data?.operations?.executionDetail ?? null;
  const askingDecisions = useTrainsAskingDecisions();
  const graph = graphResult.data?.operations?.runGraph ?? null;
  // A resume, like a requeue, runs on the saved input, so a run without one offers no resume, as
  // on the Blazor page.
  const hasInput = Boolean(e?.input?.trim());

  const nodes = useMemo(() => graph?.allNodes ?? [], [graph]);
  const selectedId = step ?? (cleared ? null : (failedNode(nodes)?.id ?? null));
  const selectedNode = nodes.find((n) => n.id === selectedId) ?? null;
  const selectedPositions = useMemo(
    () => new Set(selectedNode?.steps.map((s) => s.position) ?? []),
    [selectedNode],
  );
  const nodeOfPosition = useMemo(() => {
    const map = new Map<number, string>();
    for (const n of nodes) for (const s of n.steps) map.set(s.position, n.id);
    return map;
  }, [nodes]);

  function select(nodeId: string | null) {
    setCleared(nodeId == null);
    setStep(nodeId);
  }

  async function onCancel() {
    if (
      !(await confirmAction({
        title: "Cancel run",
        message: `Request cancellation of run #${id}? A running run stops at its next junction.`,
        danger: true,
      }))
    )
      return;
    setBusy(true);
    const r = await cancelExecution({ id });
    setBusy(false);
    const res = r.data?.operations?.cancelExecution;
    if (r.error) toast(r.error.message, "error");
    // A refusal says why: a run a user's state-machine draft started is read-only to operators.
    else if (res && !res.success) toast(res.message ?? "Could not request cancellation.", "error");
    else if (res?.count === 0) toast("Run is no longer cancellable.", "info");
    else toast("Cancellation requested.", "success");
    reexecute({ requestPolicy: "network-only" });
  }

  // Requeue queues a fresh run with this run's saved input and opens its work queue entry, as the
  // Blazor page does. askAfresh: the new run asks the model afresh instead of replaying this run's
  // decisions.
  async function onRequeue(askAfresh: boolean) {
    const what = askAfresh
      ? `Requeue run #${id}, asking afresh? The new run asks its decider again instead of replaying this run's decisions.`
      : `Requeue run #${id}? A new run of the same input is queued.`;
    if (!(await confirmAction({ title: askAfresh ? "Requeue, asking afresh" : "Requeue run", message: what }))) return;
    setBusy(true);
    const r = await requeueExecution({ id, askAfresh });
    setBusy(false);
    const res = r.data?.operations?.requeueExecution;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) {
      toast(res.message ? `Run requeued. ${res.message}` : "Run requeued.", "success");
      if (res.id != null) navigate(`/runs/queued/${res.id}`);
    } else toast(res?.message ?? "Could not requeue.", "error");
  }

  // Resume queues a run that skips to the step after this run's latest checkpoint (from null), or to
  // the node a "Resume from here" names, and opens its work queue entry. Through resumeExecution,
  // the call the Blazor page's Resume buttons make, so the API refuses the same runs with the same
  // reasons. One requeue or resume at a time.
  async function onResume(from: string | null) {
    if (busy) return;
    const what = from
      ? `Resume run #${id} from ${from}? A run is queued that skips to this step, on what the checkpoint before it restores.`
      : `Resume run #${id}? A run is queued that skips to the step after its latest checkpoint.`;
    if (!(await confirmAction({ title: "Resume run", message: what }))) return;
    setBusy(true);
    setResuming(from ?? "");
    const r = await resumeExecution({ id, from });
    setBusy(false);
    setResuming(null);
    const res = r.data?.operations?.resumeExecution;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) {
      toast(res.message ? `Run queued to resume. ${res.message}` : "Run queued to resume.", "success");
      if (res.id != null) navigate(`/runs/queued/${res.id}`);
    } else toast(res?.message ?? "Could not resume.", "error");
  }

  if (result.error)
    return (
      <>
        <BackLink to="/runs" label="All runs" />
        <p className="text-danger-fg mt-4">{result.error.message}</p>
      </>
    );
  if (result.fetching && !e) return <Loading />;
  if (!e) return <NotFound what="run" />;

  const manifestId = links?.manifestId ?? e.manifestId;
  const parentId = links?.parentId ?? e.parentId;
  const startedBy: React.ReactNode[] = [];
  if (links?.invokedBy) {
    const inv = links.invokedBy;
    const label = `${inv.machine} ${inv.instanceId}`;
    startedBy.push(
      inv.ownerKind === "SYSTEM" || inv.rowId != null ? (
        <DetailLink
          key="machine"
          to={machineInstancePath({ machine: inv.machine, ownerKind: inv.ownerKind, id: inv.instanceId, rowId: inv.rowId ?? 0 })}
        >
          Machine instance {label}
        </DetailLink>
      ) : (
        <span key="machine">Machine instance {label}</span>
      ),
    );
  }
  if (manifestId != null)
    startedBy.push(
      <DetailLink key="manifest" to={`/schedules/${manifestId}`}>
        Manifest {manifestId}
      </DetailLink>,
    );
  if (parentId != null)
    startedBy.push(
      <DetailLink key="parent" to={`/runs/${parentId}`}>
        Parent run {parentId}
      </DetailLink>,
    );
  if (links?.workQueueEntryId != null)
    startedBy.push(
      <DetailLink key="queue" to={`/runs/queued/${links.workQueueEntryId}`}>
        Queue entry {links.workQueueEntryId}
      </DetailLink>,
    );
  if (links?.deadLetterId != null)
    startedBy.push(
      <DetailLink key="dead" to={`/runs/dead-letters/${links.deadLetterId}`}>
        Retries dead letter {links.deadLetterId}
      </DetailLink>,
    );

  const duration = e.endTime ? formatMs(Date.parse(e.endTime) - Date.parse(e.startTime)) : "running";

  return (
    <div>
      <BackLink to="/runs" label="All runs" />

      <header className="mt-2 mb-6 bg-surface rounded-lg border border-line p-5">
        <div className="flex flex-wrap items-center gap-3 mb-4">
          <h1 className="text-2xl font-bold text-fg">{shortName(e.name)}</h1>
          <TermHelp term="run" />
          <StateBadge state={e.trainState} />
          {e.replayAbandoned && (
            <span
              title="Queued to replay an earlier run's decisions, it asked its deciders afresh because that replay could not be honoured."
              className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg"
            >
              Replay abandoned
            </span>
          )}
          {e.cancellationRequested && ACTIVE_STATES.has(e.trainState) && (
            <span className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg">
              Cancellation requested
            </span>
          )}
          <div className="ml-auto flex flex-wrap gap-2">
            {ACTIVE_STATES.has(e.trainState) && !e.cancellationRequested && (
              <button
                onClick={onCancel}
                disabled={busy}
                className="text-sm px-3 py-1 rounded-md border border-danger-line text-danger-fg hover:bg-danger-soft disabled:opacity-50"
              >
                Cancel
              </button>
            )}
            {hasInput && graph?.canResume && (
              <button
                onClick={() => onResume(null)}
                disabled={busy}
                title="Queue a run that skips to the step after this run's latest checkpoint, instead of running every step again."
                className="text-sm px-3 py-1 rounded-md border border-accent-line text-accent-fg hover:bg-accent-soft disabled:opacity-50"
              >
                {resuming === "" ? "Resuming…" : "Resume"}
              </button>
            )}
            {TERMINAL_STATES.has(e.trainState) && (
              <>
                <button
                  onClick={() => onRequeue(false)}
                  disabled={busy}
                  className="text-sm px-3 py-1 rounded-md bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50"
                >
                  Requeue
                </button>
                {offersDecisions(askingDecisions, [e.name]) && (
                  <button
                    onClick={() => onRequeue(true)}
                    disabled={busy}
                    title="Requeue; the new run asks the model afresh instead of replaying this run's decisions."
                    className="text-sm px-3 py-1 rounded-md border border-accent-line text-accent-fg hover:bg-accent-soft disabled:opacity-50"
                  >
                    Requeue, ask afresh
                  </button>
                )}
              </>
            )}
          </div>
        </div>

        <Fields
          rows={[
            ["Train", <DetailLink to={`/trains/${encodeURIComponent(e.name)}`}>{e.name}</DetailLink>],
            ["Duration", duration],
            ["Started", formatTime(e.startTime)],
            ["Ended", formatTime(e.endTime)],
            [
              "Started by",
              startedBy.length === 0 ? (
                "Started directly"
              ) : (
                <span className="inline-flex flex-col items-end gap-0.5">{startedBy}</span>
              ),
            ],
            ...(e.trainState === "IN_PROGRESS" && e.currentlyRunningJunction
              ? ([["Running now", e.currentlyRunningJunction]] as [string, React.ReactNode][])
              : []),
          ]}
        />
      </header>

      <ExceptionViewer
        reason={e.failureReason ?? e.failureException}
        junction={e.failureJunction}
        stackTrace={e.stackTrace}
        failureClass={e.trainState === "FAILED" ? e.failureClass : null}
      />

      {graphResult.error && !graph ? (
        // A read that failed, told apart from a run that cannot be resumed: nothing offers a resume
        // until the graph can be read.
        <section aria-label="Run graph" className="bg-surface rounded-lg border border-line p-5 mb-6">
          <h2 className="text-sm font-semibold text-fg mb-3">Run graph</h2>
          <p className="text-sm text-danger-fg">Could not read the run graph: {graphResult.error.message}</p>
        </section>
      ) : (
        graph && (
          <RunGraphPanel
            graph={graph}
            selectedId={selectedId}
            onSelect={select}
            onResume={hasInput ? (from) => void onResume(from) : undefined}
            resumingNode={resuming}
            resumeDisabled={busy}
          />
        )
      )}

      <Tabs tab={tab} onTab={setTab}>
        {(shown) => (
          <>
            <TabPanel id="timeline" shown={shown}>
              <JunctionTimeline
                metadataId={e.id}
                startTime={e.startTime}
                endTime={e.endTime}
                trainState={e.trainState}
                selected={selectedPositions}
                canSelect={(p) => nodeOfPosition.has(p)}
                onSelect={(p) => {
                  const nodeId = nodeOfPosition.get(p);
                  if (nodeId) select(nodeId);
                }}
              />
            </TabPanel>
            <TabPanel id="io" shown={shown}>
              {!e.input?.trim() && !e.output?.trim() ? (
                <p className="text-sm text-muted mb-6">
                  This run has no saved input or output. They are saved on a host that saves train
                  parameters, and a train can withhold its output.{" "}
                  <a
                    className="text-accent-fg hover:underline"
                    href="https://traxsharp.net/docs/effect"
                    target="_blank"
                    rel="noreferrer"
                  >
                    Effects
                  </a>
                </p>
              ) : (
                <>
                  <JsonPanel title="Input" json={e.input} />
                  <JsonPanel title="Output" json={e.output} />
                </>
              )}
            </TabPanel>
            <TabPanel id="logs" shown={shown}>
              <DetailPanel title="Logs">
                {/* A run's log reads top to bottom, oldest first, as on the Blazor run page. */}
                <LogsGrid metadataId={e.id} defaultOrder="OLDEST" pageSize={25} />
              </DetailPanel>
            </TabPanel>
            <TabPanel id="decisions" shown={shown}>
              <DecisionsPanel metadataId={e.id} />
            </TabPanel>
            <TabPanel id="related" shown={shown}>
              <Related e={e} links={links} />
            </TabPanel>
          </>
        )}
      </Tabs>
    </div>
  );
}

// The run's record and what it is linked to: who started it, the runs it replays or resumes, the
// queue entry and dead letter it came from, its children, and the host that ran it.
function Related({ e, links }: { e: ExecutionDetail; links: RunLinks | null }) {
  const inv = links?.invokedBy;
  return (
    <>
      <DetailPanel title="Details">
        <Fields
          rows={[
            ["Run ID", e.id],
            ["External ID", e.externalId.trim()],
            ["Scheduled time", formatTime(e.scheduledTime)],
            ["Executor", e.executor ?? "—"],
            [
              "Manifest",
              e.manifestId != null ? <DetailLink to={`/schedules/${e.manifestId}`}>{e.manifestId}</DetailLink> : "—",
            ],
            ["Parent run", e.parentId != null ? <DetailLink to={`/runs/${e.parentId}`}>{e.parentId}</DetailLink> : "—"],
            [
              "Machine instance",
              inv ? (
                inv.ownerKind === "SYSTEM" || inv.rowId != null ? (
                  <DetailLink
                    to={machineInstancePath({ machine: inv.machine, ownerKind: inv.ownerKind, id: inv.instanceId, rowId: inv.rowId ?? 0 })}
                  >
                    {inv.machine} {inv.instanceId}
                  </DetailLink>
                ) : (
                  `${inv.machine} ${inv.instanceId}`
                )
              ) : (
                "—"
              ),
            ],
            [
              "Queue entry",
              links?.workQueueEntryId != null ? (
                <DetailLink to={`/runs/queued/${links.workQueueEntryId}`}>{links.workQueueEntryId}</DetailLink>
              ) : (
                "—"
              ),
            ],
            [
              "Dead letter",
              links?.deadLetterId != null ? (
                <DetailLink to={`/runs/dead-letters/${links.deadLetterId}`}>{links.deadLetterId}</DetailLink>
              ) : (
                "—"
              ),
            ],
            ...(e.replayDecisionsOf != null
              ? ([
                  [
                    "Replays decisions of",
                    <DetailLink to={`/runs/${e.replayDecisionsOf}`}>{e.replayDecisionsOf}</DetailLink>,
                  ],
                  [
                    "Replay",
                    e.replayAbandoned
                      ? "Abandoned: it could not be honoured, so the run asked afresh"
                      : "Replayed the recorded decisions",
                  ],
                ] as [string, React.ReactNode][])
              : []),
            ...(e.resumeFrom != null
              ? ([
                  [
                    "Resumes",
                    <>
                      <DetailLink to={`/runs/${e.resumeFrom}`}>{e.resumeFrom}</DetailLink>
                      {e.resumeAt != null ? ` at ${e.resumeAt}` : " after its latest checkpoint"}
                    </>,
                  ],
                ] as [string, React.ReactNode][])
              : []),
          ]}
        />
      </DetailPanel>

      {e.childCount > 0 ? (
        <ExecutionChildren parentId={e.id} childCount={e.childCount} />
      ) : (
        <p className="text-sm text-muted mb-6">This run started no child runs.</p>
      )}

      {(e.hostName != null || e.hostEnvironment != null) && (
        <DetailPanel title="Host">
          <Fields
            rows={[
              ["Hostname", e.hostName ?? "—"],
              ["Environment", e.hostEnvironment ?? "—"],
              ["Instance ID", e.hostInstanceId ?? "—"],
            ]}
          />
          {e.hostLabels && e.hostLabels.trim() && (
            <div className="mt-3">
              <p className="text-xs text-muted mb-1">Labels</p>
              <pre className="text-xs text-fg whitespace-pre-wrap break-all font-mono">{prettyJson(e.hostLabels)}</pre>
            </div>
          )}
        </DetailPanel>
      )}
    </>
  );
}

// The tabs under the graph. A tab's panel is drawn the first time it is opened and kept after, so
// the timeline keeps following the run while another tab is shown.
function Tabs({ tab, onTab, children }: { tab: Tab; onTab: (t: Tab) => void; children: (shown: Set<Tab>) => React.ReactNode }) {
  const [opened, setOpened] = useState<Set<Tab>>(() => new Set([tab]));
  // A tab opened through the URL (Back, a link) is drawn as well.
  const shown = opened.has(tab) ? opened : new Set([...opened, tab]);
  function open(t: Tab) {
    setOpened((o) => (o.has(t) ? o : new Set([...o, t])));
    onTab(t);
  }
  return (
    <TabContext.Provider value={tab}>
      <div role="tablist" aria-label="Run sections" className="flex flex-wrap gap-1 border-b border-line mb-4">
        {TABS.map(([key, label]) => (
          <button
            key={key}
            role="tab"
            id={`run-tab-${key}`}
            aria-selected={tab === key}
            aria-controls={`run-panel-${key}`}
            onClick={() => open(key)}
            className={`px-3 py-1.5 text-sm -mb-px border-b-2 ${
              tab === key ? "border-accent text-fg font-medium" : "border-transparent text-muted hover:text-fg"
            }`}
          >
            {label}
          </button>
        ))}
      </div>
      {children(shown)}
    </TabContext.Provider>
  );
}

const TabContext = createContext<Tab>("timeline");

function TabPanel({ id, shown, children }: { id: Tab; shown: Set<Tab>; children: React.ReactNode }) {
  const tab = useContext(TabContext);
  if (!shown.has(id)) return null;
  return (
    <div role="tabpanel" id={`run-panel-${id}`} aria-labelledby={`run-tab-${id}`} hidden={tab !== id}>
      {children}
    </div>
  );
}
