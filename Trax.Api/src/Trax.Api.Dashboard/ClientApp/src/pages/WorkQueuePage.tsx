import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { WORK_QUEUE } from "../graphql/queries";
import {
  CANCEL_WORK_QUEUE_ENTRIES,
  CANCEL_WORK_QUEUE_ENTRY,
} from "../graphql/mutations";
import { Pager } from "../components/Pager";
import { QueueTrainDialog } from "../components/QueueTrainDialog";
import { useKeyset } from "../lib/useKeyset";
import { useSelection } from "../lib/useSelection";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { toast } from "../lib/toast";
import type { PagedResult, WorkQueueStatus, WorkQueueSummary } from "../types";

const PAGE_SIZE = 25;
const STATUSES: (WorkQueueStatus | "")[] = ["", "QUEUED", "DISPATCHED", "CANCELLED"];

const STATUS_STYLE: Record<WorkQueueStatus, string> = {
  QUEUED: "bg-warn-soft text-warn-fg",
  DISPATCHED: "bg-info-soft text-info-fg",
  CANCELLED: "bg-raised text-fg-2",
};

interface WorkQueueData {
  operations: { workQueue: { workQueues: PagedResult<WorkQueueSummary> } };
}

export function WorkQueuePage() {
  const { afterId, isFirstPage, next, prev, reset } = useKeyset();
  const [status, setStatus] = useState<WorkQueueStatus | "">("");
  const [trainName, setTrainName] = useState("");
  // A subject's queue (exact key) and a manifest's entries. Both can arrive in the URL, so a
  // manifest or a subject elsewhere can link to its entries.
  const [params] = useSearchParams();
  const [subjectKey, setSubjectKey] = useState(params.get("subjectKey") ?? "");
  const [manifestId, setManifestId] = useState((params.get("manifestId") ?? "").replace(/\D/g, ""));
  const [showQueue, setShowQueue] = useState(false);
  const { selected, toggle, setMany, clear } = useSelection();
  const [busy, setBusy] = useState(false);

  const [result, reexecute] = useQuery<WorkQueueData>({
    query: WORK_QUEUE,
    variables: {
      take: PAGE_SIZE,
      afterId,
      status: status || null,
      trainName: trainName || null,
      subjectKey: subjectKey || null,
      manifestId: manifestId ? Number(manifestId) : null,
    },
  });
  const [, cancelEntry] = useMutation(CANCEL_WORK_QUEUE_ENTRY);
  const [, cancelMany] = useMutation(CANCEL_WORK_QUEUE_ENTRIES);
  useRefetchOnChange("WORK_QUEUE", () => reexecute({ requestPolicy: "network-only" }));
  usePoll(() => reexecute({ requestPolicy: "network-only" }));

  // Chain optionally: a GraphQL error can return { operations: null }.
  const page = result.data?.operations?.workQueue?.workQueues;
  const refetch = () => {
    clear();
    reexecute({ requestPolicy: "network-only" });
  };

  const queued = (page?.items ?? []).filter((w) => w.status === "QUEUED");
  const selectedIds = [...selected];
  const allSelected = queued.length > 0 && queued.every((w) => selected.has(w.id));

  async function onCancel(id: number) {
    if (!confirm(`Cancel queued entry #${id}?`)) return;
    const r = await cancelEntry({ id });
    const res = r.data?.operations?.workQueue?.cancelWorkQueueEntry;
    if (r.error) toast(r.error.message, "error");
    // A refusal says why: an entry a user's state-machine draft queued is read-only to operators.
    else if (!res?.success) toast(res?.message ?? "Could not cancel.", "error");
    else toast(`Entry #${id} cancelled.`, "success");
    refetch();
  }

  async function onBulkCancel() {
    if (selectedIds.length === 0) return;
    if (!confirm(`Cancel ${selectedIds.length} queued entr(ies)?`)) return;
    setBusy(true);
    const r = await cancelMany({ ids: selectedIds });
    setBusy(false);
    const res = r.data?.operations?.workQueue?.cancelWorkQueueEntries;
    if (r.error) toast(r.error.message, "error");
    else if (!res?.success) toast(res?.message ?? "Could not cancel.", "error");
    // The API's message counts what it cancelled and says why it skipped any.
    else toast(res.message ?? `${res.count ?? 0} entr(ies) cancelled.`, "success");
    refetch();
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-4">
        <h1 className="text-2xl font-bold text-fg">Work queue</h1>
        <div className="flex gap-2">
          <button
            onClick={() => setShowQueue(true)}
            className="text-sm px-3 py-1 rounded-md bg-accent text-on-accent hover:bg-accent-hover"
          >
            Queue train
          </button>
          <input
            value={trainName}
            onChange={(e) => {
              setTrainName(e.target.value);
              clear();
              reset();
            }}
            placeholder="Filter by train name…"
            className="text-sm border border-line-strong rounded-md px-2 py-1 w-64"
          />
          <input
            aria-label="Subject"
            value={subjectKey}
            onChange={(e) => {
              setSubjectKey(e.target.value);
              clear();
              reset();
            }}
            placeholder="Subject (exact)…"
            title="Only entries serialized against this subject"
            className="text-sm border border-line-strong rounded-md px-2 py-1 w-40"
          />
          <input
            aria-label="Manifest id"
            value={manifestId}
            onChange={(e) => {
              setManifestId(e.target.value.replace(/\D/g, ""));
              clear();
              reset();
            }}
            placeholder="Manifest id…"
            className="text-sm border border-line-strong rounded-md px-2 py-1 w-28"
          />
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as WorkQueueStatus | "");
              clear();
              reset();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {s === "" ? "All statuses" : label(s)}
              </option>
            ))}
          </select>
        </div>
      </div>

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      {selectedIds.length > 0 && (
        <div className="flex items-center gap-3 mb-3 px-4 py-2 rounded-lg bg-accent-soft border border-accent-line text-sm">
          <span className="text-accent-fg font-medium">
            {selectedIds.length} selected
          </span>
          <button
            onClick={onBulkCancel}
            disabled={busy}
            className="text-danger-fg font-medium hover:underline disabled:opacity-50"
          >
            Cancel selected
          </button>
          <button
            onClick={clear}
            className="ml-auto text-muted hover:underline"
          >
            Clear
          </button>
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 w-8">
                <input
                  type="checkbox"
                  aria-label="Select all"
                  checked={allSelected}
                  disabled={queued.length === 0}
                  onChange={(e) =>
                    setMany(
                      queued.map((w) => w.id),
                      e.target.checked,
                    )
                  }
                />
              </th>
              <th className="px-4 py-2 font-medium">Train</th>
              <th className="px-4 py-2 font-medium">Status</th>
              <th className="px-4 py-2 font-medium">Created</th>
              <th className="px-4 py-2 font-medium">Priority</th>
              <th className="px-4 py-2 font-medium">Attempts</th>
              <th className="px-4 py-2 font-medium">Subject</th>
              <th className="px-4 py-2 font-medium">Confirmed</th>
              <th className="px-4 py-2 font-medium" title="The run whose recorded decisions this entry's run replays">
                Replays
              </th>
              <th className="px-4 py-2 font-medium text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {page?.items.map((w) => (
              <tr key={w.id}>
                <td className="px-4 py-2">
                  {w.status === "QUEUED" && (
                    <input
                      type="checkbox"
                      aria-label={`Select #${w.id}`}
                      checked={selected.has(w.id)}
                      onChange={() => toggle(w.id)}
                    />
                  )}
                </td>
                <td className="px-4 py-2" title={w.trainName}>
                  <Link
                    to={`/work-queue/${w.id}`}
                    className="font-medium text-accent-fg hover:underline"
                  >
                    {shortName(w.trainName)}
                  </Link>
                </td>
                <td className="px-4 py-2">
                  <span
                    className={`text-xs px-2 py-0.5 rounded-full ${STATUS_STYLE[w.status]}`}
                  >
                    {label(w.status)}
                  </span>
                </td>
                <td className="px-4 py-2 text-fg-2">
                  {new Date(w.createdAt).toLocaleString()}
                </td>
                <td className="px-4 py-2 text-fg-2">{w.priority}</td>
                <td className="px-4 py-2 text-fg-2">{w.dispatchAttempts}</td>
                <td className="px-4 py-2 text-fg-2 max-w-[10rem] truncate" title={w.subjectKey ?? ""}>
                  {w.subjectKey ? (
                    <button
                      onClick={() => {
                        setSubjectKey(w.subjectKey!);
                        clear();
                        reset();
                      }}
                      title="Show this subject's queue"
                      className="text-accent-fg hover:underline"
                    >
                      {w.subjectKey}
                    </button>
                  ) : (
                    "—"
                  )}
                </td>
                <td className="px-4 py-2 text-fg-2">
                  <Confirmed entry={w} />
                </td>
                <td className="px-4 py-2 text-xs">
                  {w.replayDecisionsOf != null ? (
                    <Link to={`/executions/${w.replayDecisionsOf}`} className="text-accent-fg hover:underline">
                      #{w.replayDecisionsOf}
                    </Link>
                  ) : (
                    <span className="text-faint">—</span>
                  )}
                </td>
                <td className="px-4 py-2 text-right whitespace-nowrap">
                  {w.status === "QUEUED" ? (
                    <button
                      onClick={() => onCancel(w.id)}
                      className="text-danger-fg hover:underline"
                    >
                      Cancel
                    </button>
                  ) : (
                    <span className="text-faint">—</span>
                  )}
                </td>
              </tr>
            ))}
            {page?.items.length === 0 && (
              <tr>
                <td colSpan={10} className="px-4 py-8 text-center text-muted">
                  No entries.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <Pager
        total={page?.totalCount}
        isEstimated={page?.isEstimatedCount}
        isFirstPage={isFirstPage}
        nextCursor={page?.nextCursor}
        onPrev={prev}
        onNext={next}
      />

      {showQueue && (
        <QueueTrainDialog onClose={() => setShowQueue(false)} onQueued={refetch} />
      )}
    </div>
  );
}

// An entry staged by a two-phase enqueue is queued but not yet dispatchable; it is normally
// confirmed moments later, once its OnQueue hook returns. Only a queued entry can be staged: a
// cancelled one keeps a null confirmedAt but is simply cancelled. Rows from before the field
// existed (undefined) show nothing.
function Confirmed({ entry }: { entry: WorkQueueSummary }) {
  if (entry.confirmedAt === null && entry.status === "QUEUED")
    return (
      <span
        title="Staged until its OnQueue hook returns"
        className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg"
      >
        Staged
      </span>
    );
  return <>{entry.confirmedAt ? "Yes" : "—"}</>;
}

function label(s: string): string {
  return s
    .toLowerCase()
    .replace(/_/g, " ")
    .replace(/^\w/, (c) => c.toUpperCase());
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}
