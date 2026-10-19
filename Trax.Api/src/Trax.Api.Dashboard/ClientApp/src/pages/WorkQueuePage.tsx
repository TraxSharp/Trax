import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { WORK_QUEUE } from "../graphql/queries";
import {
  CANCEL_WORK_QUEUE_ENTRIES,
  CANCEL_WORK_QUEUE_ENTRY,
} from "../graphql/mutations";
import { QueueTrainDialog } from "../components/QueueTrainDialog";
import { useKeyset } from "../lib/useKeyset";
import { useSelection } from "../lib/useSelection";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { toast } from "../lib/toast";
import type { PagedResult, WorkQueueStatus, WorkQueueSummary } from "../types";
import { confirmAction } from "../lib/confirm";
import { useUrlState } from "../lib/urlState";
import { TermHelp } from "../components/TermHelp";
import { RunsTabs } from "../components/RunsTabs";
import { BatchBar, EmptyState, ListView, MaybeIdLink, type Column } from "../components/ListView";
import { enumLabel, shortName } from "../lib/format";

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
  const [status, setStatus] = useUrlState<WorkQueueStatus | "">("status");
  const [trainName, setTrainName] = useUrlState("train");
  // A subject's queue (exact key) and a manifest's entries. Both can arrive in the URL, so a
  // manifest or a subject elsewhere can link to its entries.
  const [subjectKey, setSubjectKey] = useUrlState("subjectKey");
  const [manifestIdParam, setManifestId] = useUrlState("manifestId");
  const manifestId = manifestIdParam.replace(/\D/g, "");
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

  const selectedIds = [...selected];
  const hasFilter = status !== "" || trainName !== "" || subjectKey !== "" || manifestId !== "";
  const showSubject = (key: string) => {
    setSubjectKey(key);
    clear();
    reset();
  };

  async function onCancel(id: number) {
    if (!(await confirmAction({ title: "Cancel queued entry", message: `Cancel queued entry #${id}? Its run never starts.`, danger: true }))) return;
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
    if (!(await confirmAction({ title: "Cancel queued entries", message: `Cancel ${selectedIds.length} queued entr(ies)? Their runs never start.`, danger: true }))) return;
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

  const columns: Column<WorkQueueSummary>[] = [
    {
      header: "Train",
      cell: (w) => (
        <Link to={`/runs/queued/${w.id}`} title={w.trainName} className="font-medium text-accent-fg hover:underline">
          {shortName(w.trainName)}
        </Link>
      ),
    },
    { header: "Entry", cell: (w) => <MaybeIdLink to={(id) => `/runs/queued/${id}`} id={w.id} />, className: "text-xs" },
    {
      header: "Status",
      cell: (w) => <span className={`text-xs px-2 py-0.5 rounded-full ${STATUS_STYLE[w.status]}`}>{enumLabel(w.status)}</span>,
    },
    { header: "Created", cell: (w) => new Date(w.createdAt).toLocaleString(), className: "text-fg-2 whitespace-nowrap" },
    { header: "Priority", cell: (w) => w.priority, className: "text-fg-2" },
    { header: "Attempts", cell: (w) => w.dispatchAttempts, className: "text-fg-2" },
    {
      header: "Subject",
      title: "Entries with one subject run one at a time; click to see that subject's queue",
      cell: (w) =>
        w.subjectKey ? (
          <button
            onClick={() => showSubject(w.subjectKey!)}
            title="Show this subject's queue"
            className="text-accent-fg hover:underline"
          >
            {w.subjectKey}
          </button>
        ) : (
          "—"
        ),
      className: "text-fg-2 max-w-[10rem] truncate",
    },
    { header: "Confirmed", cell: (w) => <Confirmed entry={w} />, className: "text-fg-2" },
    {
      header: "Manifest",
      title: "The manifest that queued the entry",
      cell: (w) => <MaybeIdLink to={(id) => `/schedules/${id}`} id={w.manifestId} />,
      className: "text-xs",
    },
    {
      header: "Run",
      title: "The run the entry started, once dispatched",
      cell: (w) => <MaybeIdLink to={(id) => `/runs/${id}`} id={w.metadataId} />,
      className: "text-xs",
    },
    {
      header: "Replays",
      title: "The run whose recorded decisions this entry's run replays",
      cell: (w) => <MaybeIdLink to={(id) => `/runs/${id}`} id={w.replayDecisionsOf} />,
      className: "text-xs",
    },
    {
      header: "Actions",
      cell: (w) =>
        w.status === "QUEUED" ? (
          <button onClick={() => onCancel(w.id)} className="text-danger-fg hover:underline">
            Cancel
          </button>
        ) : (
          <span className="text-faint">—</span>
        ),
      className: "text-right whitespace-nowrap",
    },
  ];

  return (
    <div>
      <RunsTabs />
      <div className="flex flex-wrap items-center justify-between mb-6 gap-4">
        <div className="flex items-center gap-2">
          <h1 className="text-2xl font-bold text-fg">Queued</h1>
          <TermHelp term="workQueue" />
        </div>
        <div className="flex flex-wrap gap-2">
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
            aria-label="Status"
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
                {s === "" ? "All statuses" : enumLabel(s)}
              </option>
            ))}
          </select>
        </div>
      </div>

      <BatchBar count={selectedIds.length} onClear={clear}>
        <button
          onClick={onBulkCancel}
          disabled={busy}
          className="text-danger-fg font-medium hover:underline disabled:opacity-50"
        >
          Cancel selected
        </button>
      </BatchBar>

      <ListView
        label="Queued entries"
        columns={columns}
        rows={page?.items}
        selection={{ selected, toggle, setMany, can: (w) => w.status === "QUEUED" }}
        error={result.error?.message}
        empty={
          <EmptyState title="No entries." docs={QUEUE_DOCS}>
            {hasFilter
              ? "No entry matches these filters."
              : "An entry is listed here while a queued run waits for a worker, and after, until it is cleaned up."}
          </EmptyState>
        }
        paging={{
          total: page?.totalCount,
          isEstimated: page?.isEstimatedCount,
          isFirstPage,
          nextCursor: page?.nextCursor,
          onPrev: prev,
          onNext: next,
        }}
      />

      {showQueue && (
        <QueueTrainDialog onClose={() => setShowQueue(false)} onQueued={refetch} />
      )}
    </div>
  );
}

const QUEUE_DOCS = "https://traxsharp.net/docs/scheduler/job-submission";

// An entry staged by a two-phase enqueue is queued but not yet dispatchable; it is normally
// confirmed moments later, once its OnQueue hook returns. Only a queued entry can be staged: a
// cancelled one keeps a null confirmedAt but is simply cancelled. Rows from before the field
// existed (undefined) show nothing.
function Confirmed({ entry }: { entry: WorkQueueSummary }) {
  if (entry.confirmedAt === null && entry.status === "QUEUED")
    return (
      <span
        title="Staged until the train's queue check finishes"
        className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg"
      >
        Staged
      </span>
    );
  return <>{entry.confirmedAt ? "Yes" : "—"}</>;
}

