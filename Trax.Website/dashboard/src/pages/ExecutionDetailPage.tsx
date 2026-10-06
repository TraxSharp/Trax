import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { EXECUTION_DETAIL } from "../graphql/queries";
import { CANCEL_EXECUTION, REQUEUE_EXECUTION } from "../graphql/mutations";
import { StateBadge } from "../components/StateBadge";
import { StateTimeline } from "../components/StateTimeline";
import { ExecutionChildren } from "../components/ExecutionChildren";
import { JunctionTimeline } from "../components/JunctionTimeline";
import { DecisionsPanel } from "../components/DecisionsPanel";
import { LogsGrid } from "../components/LogsGrid";
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
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import type { ExecutionDetail } from "../types";

const ACTIVE_STATES = new Set(["PENDING", "IN_PROGRESS"]);
const TERMINAL_STATES = new Set(["COMPLETED", "FAILED", "CANCELLED"]);

interface ExecData {
  operations: { executionDetail: ExecutionDetail | null };
}

export function ExecutionDetailPage() {
  const id = Number(useParams().id);
  const [result, reexecute] = useQuery<ExecData>({
    query: EXECUTION_DETAIL,
    variables: { id },
  });
  useRefetchOnChange("EXECUTION", () => reexecute({ requestPolicy: "network-only" }));
  const [, cancelExecution] = useMutation(CANCEL_EXECUTION);
  const [, requeueExecution] = useMutation(REQUEUE_EXECUTION);
  const [busy, setBusy] = useState(false);
  const navigate = useNavigate();

  const e = result.data?.operations.executionDetail;

  async function onCancel() {
    if (!confirm(`Request cancellation of execution #${id}?`)) return;
    setBusy(true);
    const r = await cancelExecution({ id });
    setBusy(false);
    if (r.error) toast(r.error.message, "error");
    else if (r.data?.operations.cancelExecution.count === 0)
      toast("Execution is no longer cancellable.", "info");
    else toast("Cancellation requested.", "success");
    reexecute({ requestPolicy: "network-only" });
  }

  // Re-queue queues a fresh run with this run's saved input and opens its work queue entry, as the
  // Blazor page does. askAfresh: the new run asks the model afresh instead of replaying this run's
  // decisions.
  async function onRequeue(askAfresh: boolean) {
    const what = askAfresh
      ? `Re-queue execution #${id}, asking afresh? The new run asks the model again instead of replaying this run's decisions.`
      : `Re-queue execution #${id}? A fresh run will be enqueued.`;
    if (!confirm(what)) return;
    setBusy(true);
    const r = await requeueExecution({ id, askAfresh });
    setBusy(false);
    const res = r.data?.operations?.requeueExecution;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) {
      toast(res.message ? `Execution re-queued. ${res.message}` : "Execution re-queued.", "success");
      if (res.id != null) navigate(`/work-queue/${res.id}`);
    } else toast(res?.message ?? "Could not re-queue.", "error");
  }

  if (result.error)
    return (
      <>
        <BackLink to="/executions" label="All executions" />
        <p className="text-danger-fg mt-4">{result.error.message}</p>
      </>
    );
  if (result.fetching && !e) return <Loading />;
  if (!e) return <NotFound what="execution" />;

  return (
    <div>
      <BackLink to="/executions" label="All executions" />
      <div className="flex items-center gap-3 mt-2 mb-6">
        <h1 className="text-2xl font-bold text-fg">
          {shortName(e.name)}
        </h1>
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
        <div className="ml-auto flex gap-2">
          {ACTIVE_STATES.has(e.trainState) && !e.cancellationRequested && (
            <button
              onClick={onCancel}
              disabled={busy}
              className="text-sm px-3 py-1 rounded-md border border-danger-line text-danger-fg hover:bg-danger-soft disabled:opacity-50"
            >
              Cancel
            </button>
          )}
          {TERMINAL_STATES.has(e.trainState) && (
            <>
              <button
                onClick={() => onRequeue(false)}
                disabled={busy}
                className="text-sm px-3 py-1 rounded-md bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50"
              >
                Re-queue
              </button>
              <button
                onClick={() => onRequeue(true)}
                disabled={busy}
                title="Re-queue; the new run asks the model afresh instead of replaying this run's decisions."
                className="text-sm px-3 py-1 rounded-md border border-accent-line text-accent-fg hover:bg-accent-soft disabled:opacity-50"
              >
                Re-queue, ask afresh
              </button>
            </>
          )}
        </div>
      </div>

      <StateTimeline state={e.trainState} startTime={e.startTime} endTime={e.endTime} />

      <ExceptionViewer
        reason={e.failureReason ?? e.failureException}
        junction={e.failureJunction}
        stackTrace={e.stackTrace}
        failureClass={e.trainState === "FAILED" ? e.failureClass : null}
      />

      <DetailPanel title="Details">
        <Fields
          rows={[
            ["ID", e.id],
            ["External ID", e.externalId.trim()],
            ["Name", e.name],
            ["State", e.trainState],
            ["Started", formatTime(e.startTime)],
            ["Ended", formatTime(e.endTime)],
            [
              "Duration",
              e.endTime
                ? formatMs(Date.parse(e.endTime) - Date.parse(e.startTime))
                : "—",
            ],
            ["Scheduled time", formatTime(e.scheduledTime)],
            [
              "Manifest ID",
              e.manifestId != null ? (
                <DetailLink to={`/manifests/${e.manifestId}`}>{e.manifestId}</DetailLink>
              ) : (
                "—"
              ),
            ],
            [
              "Parent ID",
              e.parentId != null ? (
                <DetailLink to={`/executions/${e.parentId}`}>{e.parentId}</DetailLink>
              ) : (
                "—"
              ),
            ],
            ["Executor", e.executor ?? "—"],
            ...(e.replayDecisionsOf != null
              ? ([
                  [
                    "Replays decisions of",
                    <DetailLink to={`/executions/${e.replayDecisionsOf}`}>
                      {e.replayDecisionsOf}
                    </DetailLink>,
                  ],
                  [
                    "Replay",
                    e.replayAbandoned
                      ? "Abandoned: it could not be honoured, so the run asked afresh"
                      : "Replayed the recorded decisions",
                  ],
                ] as [string, React.ReactNode][])
              : []),
          ]}
        />
      </DetailPanel>

      {(e.hostName != null || e.hostEnvironment != null) && (
        <DetailPanel title="Execution host">
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
              <pre className="text-xs text-fg whitespace-pre-wrap break-all font-mono">
                {prettyJson(e.hostLabels)}
              </pre>
            </div>
          )}
        </DetailPanel>
      )}

      {e.trainState === "IN_PROGRESS" && e.currentlyRunningJunction && (
        <div className="bg-info-soft border-l-4 border-info rounded-lg p-5 mb-6">
          <h2 className="text-sm font-semibold text-info-fg mb-3">
            Junction progress
          </h2>
          <Fields
            rows={[
              ["Currently running", e.currentlyRunningJunction],
              [
                "Junction started",
                e.junctionStartedAt ? new Date(e.junctionStartedAt).toLocaleTimeString() : "—",
              ],
            ]}
          />
        </div>
      )}

      <JunctionTimeline
        metadataId={e.id}
        startTime={e.startTime}
        endTime={e.endTime}
        trainState={e.trainState}
      />

      <JsonPanel title="Input" json={e.input} />
      <JsonPanel title="Output" json={e.output} />

      {e.childCount > 0 && (
        <ExecutionChildren parentId={e.id} childCount={e.childCount} />
      )}

      <DecisionsPanel metadataId={e.id} />

      <DetailPanel title="Logs">
        {/* A run's log reads top to bottom, oldest first, as on the Blazor run page. */}
        <LogsGrid metadataId={e.id} defaultOrder="OLDEST" pageSize={25} />
      </DetailPanel>
    </div>
  );
}

