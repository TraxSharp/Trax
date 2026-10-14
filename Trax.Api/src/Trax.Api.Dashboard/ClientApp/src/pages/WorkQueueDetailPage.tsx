import { useState } from "react";
import { useParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { WORK_QUEUE_DETAIL } from "../graphql/queries";
import { CANCEL_WORK_QUEUE_ENTRY } from "../graphql/mutations";
import {
  BackLink,
  DetailLink,
  DetailPanel,
  Fields,
  JsonPanel,
  Loading,
  NotFound,
} from "../components/detail";
import { enumLabel, formatTime, shortName } from "../lib/format";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { toast } from "../lib/toast";
import type { WorkQueueDetail } from "../types";

interface WqData {
  operations: { workQueue: { detail: WorkQueueDetail | null } };
}

// One work queue entry, read through workQueue.detail: its fields, its input (sensitive values
// masked by the API), and, for a subject-keyed entry, the entry it waits on. Mirrors the Blazor
// WorkQueueDetailPage.
export function WorkQueueDetailPage() {
  const id = Number(useParams().id);
  const [result, reexecute] = useQuery<WqData>({
    query: WORK_QUEUE_DETAIL,
    variables: { id },
  });
  useRefetchOnChange("WORK_QUEUE", () => reexecute({ requestPolicy: "network-only" }));
  const [, cancelEntry] = useMutation(CANCEL_WORK_QUEUE_ENTRY);
  const [busy, setBusy] = useState(false);
  const w = result.data?.operations?.workQueue?.detail;

  if (result.error && !w)
    return (
      <>
        <BackLink to="/work-queue" label="Work queue" />
        <p className="text-danger-fg mt-4">{result.error.message}</p>
      </>
    );
  if (result.fetching && !w) return <Loading />;
  if (!w) return <NotFound what="work queue entry" />;

  async function onCancel() {
    if (!confirm(`Cancel queued entry #${id}?`)) return;
    setBusy(true);
    const r = await cancelEntry({ id });
    setBusy(false);
    const res = r.data?.operations?.workQueue?.cancelWorkQueueEntry;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(`Entry #${id} cancelled.`, "success");
    else toast(res?.message ?? "Could not cancel.", "error");
    reexecute({ requestPolicy: "network-only" });
  }

  const waitingOn =
    w.subjectHeldBy != null
      ? { id: w.subjectHeldBy, why: "which is running for the same subject" }
      : w.subjectQueuedBehind != null
        ? { id: w.subjectQueuedBehind, why: "which is ahead of it for the same subject" }
        : null;

  return (
    <div>
      <BackLink to="/work-queue" label="Work queue" />
      <div className="flex items-center gap-3 mt-2 mb-6">
        <h1 className="text-2xl font-bold text-fg">
          {shortName(w.trainName)}
        </h1>
        {w.status === "QUEUED" && (
          <button
            onClick={onCancel}
            disabled={busy}
            className="ml-auto text-sm px-3 py-1 rounded-md border border-danger-line text-danger-fg hover:bg-danger-soft disabled:opacity-50"
          >
            Cancel entry
          </button>
        )}
      </div>

      <DetailPanel title="Details">
        <Fields
          rows={[
            ["ID", w.id],
            ["External ID", w.externalId],
            ["Train", w.trainName],
            ["Status", enumLabel(w.status)],
            ["Priority", w.priority],
            ["Dispatch attempts", w.dispatchAttempts],
            ["Created", formatTime(w.createdAt)],
            ["Scheduled at", formatTime(w.scheduledAt)],
            ["Dispatched", formatTime(w.dispatchedAt)],
            [
              "Confirmed at",
              w.confirmedAt
                ? formatTime(w.confirmedAt)
                : w.status === "QUEUED"
                  ? "Not yet: staged until its OnQueue hook returns"
                  : "—",
            ],
            [
              "Subject",
              w.subjectKey ? (
                <DetailLink to={`/work-queue?subjectKey=${encodeURIComponent(w.subjectKey)}`}>{w.subjectKey}</DetailLink>
              ) : (
                "—"
              ),
            ],
            ...(waitingOn
              ? ([
                  [
                    "Waiting on",
                    <span>
                      <DetailLink to={`/work-queue/${waitingOn.id}`}>Entry {waitingOn.id}</DetailLink>
                      , {waitingOn.why}
                    </span>,
                  ],
                ] as [string, React.ReactNode][])
              : []),
            ["Input type", w.inputTypeName ?? "—"],
            [
              "Manifest",
              w.manifestId != null ? (
                <DetailLink to={`/manifests/${w.manifestId}`}>#{w.manifestId}</DetailLink>
              ) : (
                "—"
              ),
            ],
            [
              "Execution",
              w.metadataId != null ? (
                <DetailLink to={`/executions/${w.metadataId}`}>#{w.metadataId}</DetailLink>
              ) : (
                "—"
              ),
            ],
            [
              "Replays decisions of",
              w.replayDecisionsOf != null ? (
                <DetailLink to={`/executions/${w.replayDecisionsOf}`}>#{w.replayDecisionsOf}</DetailLink>
              ) : (
                "None: it asks afresh"
              ),
            ],
            [
              "Dead letter",
              w.deadLetterId != null ? (
                <DetailLink to={`/dead-letters/${w.deadLetterId}`}>#{w.deadLetterId}</DetailLink>
              ) : (
                "—"
              ),
            ],
          ]}
        />
      </DetailPanel>

      <JsonPanel title="Input" json={w.input} />
    </div>
  );
}
