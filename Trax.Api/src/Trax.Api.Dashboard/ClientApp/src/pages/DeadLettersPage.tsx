import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useClient, useMutation, useQuery } from "urql";
import { DEAD_LETTERS, REQUEUE_ALL_JOB } from "../graphql/queries";
import {
  ACKNOWLEDGE_ALL_DEAD_LETTERS,
  ACKNOWLEDGE_DEAD_LETTER,
  ACKNOWLEDGE_DEAD_LETTERS,
  REQUEUE_ALL_DEAD_LETTERS,
  REQUEUE_DEAD_LETTER,
  REQUEUE_DEAD_LETTERS,
} from "../graphql/mutations";
import { Pager } from "../components/Pager";
import { NotePrompt } from "../components/NotePrompt";
import { useKeyset } from "../lib/useKeyset";
import { useSelection } from "../lib/useSelection";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { toast } from "../lib/toast";
import type { DeadLetterStatus, DeadLetterSummary, PagedResult } from "../types";

const PAGE_SIZE = 25;
const STATUSES: (DeadLetterStatus | "")[] = [
  "",
  "AWAITING_INTERVENTION",
  "RETRIED",
  "ACKNOWLEDGED",
];

const STATUS_STYLE: Record<DeadLetterStatus, string> = {
  AWAITING_INTERVENTION: "bg-danger-soft text-danger-fg",
  RETRIED: "bg-info-soft text-info-fg",
  ACKNOWLEDGED: "bg-raised text-fg-2",
};

interface RequeueAllJob {
  id: string;
  status: "RUNNING" | "SUCCEEDED" | "FAILED" | "CANCELED";
  // How many dead letters were awaiting when the job started.
  awaitingAtStart?: number;
  count: number | null;
  message: string;
}

const JOB_POLL_MS = 1000;

interface DeadLettersData {
  operations: { deadLetters: { deadLetters: PagedResult<DeadLetterSummary> } };
}

export function DeadLettersPage() {
  const { afterId, isFirstPage, next, prev, reset } = useKeyset();
  const [status, setStatus] = useState<DeadLetterStatus | "">("");
  // One manifest's dead letters; the dead-letter page links here with ?manifestId=.
  const [params] = useSearchParams();
  const [manifestId, setManifestId] = useState((params.get("manifestId") ?? "").replace(/\D/g, ""));
  const { selected, toggle, setMany, clear } = useSelection();
  // note dialog: single dead-letter id, "bulk" (selection), "all" (every unresolved), or null.
  const [ackTarget, setAckTarget] = useState<number | "bulk" | "all" | null>(null);
  const [busy, setBusy] = useState(false);

  const [result, reexecute] = useQuery<DeadLettersData>({
    query: DEAD_LETTERS,
    variables: {
      take: PAGE_SIZE,
      afterId,
      status: status || null,
      manifestId: manifestId ? Number(manifestId) : null,
    },
  });
  const [, requeue] = useMutation(REQUEUE_DEAD_LETTER);
  const [, acknowledge] = useMutation(ACKNOWLEDGE_DEAD_LETTER);
  const [, requeueMany] = useMutation(REQUEUE_DEAD_LETTERS);
  const [, acknowledgeMany] = useMutation(ACKNOWLEDGE_DEAD_LETTERS);
  const [, requeueAll] = useMutation(REQUEUE_ALL_DEAD_LETTERS);
  const client = useClient();
  const [, acknowledgeAll] = useMutation(ACKNOWLEDGE_ALL_DEAD_LETTERS);
  useRefetchOnChange("DEAD_LETTER", () => reexecute({ requestPolicy: "network-only" }));
  usePoll(() => reexecute({ requestPolicy: "network-only" }));

  // Chain optionally: a GraphQL error can return { operations: null }.
  const page = result.data?.operations?.deadLetters?.deadLetters;
  const refetch = () => {
    clear();
    reexecute({ requestPolicy: "network-only" });
  };

  const actionable = (page?.items ?? []).filter(
    (d) => d.status === "AWAITING_INTERVENTION",
  );
  const selectedIds = [...selected];
  const allSelected =
    actionable.length > 0 && actionable.every((d) => selected.has(d.id));

  async function onRequeue(id: number) {
    if (!confirm(`Requeue dead letter #${id}? A new execution will be enqueued.`))
      return;
    const r = await requeue({ id });
    const res = r.data?.operations?.deadLetters?.requeueDeadLetter;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(`Dead letter #${id} requeued.`, "success");
    else toast(res?.message ?? "Could not requeue.", "error");
    refetch();
  }

  // askAfresh: each new run asks the model afresh instead of replaying the failed run's decisions.
  async function onBulkRequeue(askAfresh: boolean) {
    if (selectedIds.length === 0) return;
    if (
      !confirm(
        askAfresh
          ? `Requeue ${selectedIds.length} dead letter(s), asking afresh?`
          : `Requeue ${selectedIds.length} dead letter(s)?`,
      )
    )
      return;
    setBusy(true);
    const r = await requeueMany({ ids: selectedIds, askAfresh });
    setBusy(false);
    if (r.error) toast(r.error.message, "error");
    else {
      const c = r.data?.operations.deadLetters.requeueDeadLetters.count ?? 0;
      toast(
        c > 0 ? `${c} dead letter(s) requeued.` : "Nothing to requeue.",
        c > 0 ? "success" : "info",
      );
    }
    refetch();
  }

  async function onRequeueAll(askAfresh: boolean) {
    if (
      !confirm(
        askAfresh
          ? "Requeue every unresolved dead letter, asking afresh? Each new run asks the model again instead of replaying the failed run's decisions."
          : "Requeue every unresolved dead letter? A new execution is enqueued for each.",
      )
    )
      return;
    setBusy(true);
    const r = await requeueAll({ askAfresh });
    let job: RequeueAllJob | null | undefined = r.data?.operations?.deadLetters.requeueAllDeadLetters;
    if (r.error || !job) {
      setBusy(false);
      toast(r.error?.message ?? "Requeue all failed.", "error");
      return;
    }
    const started = job;
    // The server requeues in the background; follow the job until it stops running. A null read
    // means this node no longer knows the job (restart, or another node behind a balancer), so
    // stop following it and let the list show where the backlog stands.
    while (job && job.status === "RUNNING") {
      await new Promise((resolve) => setTimeout(resolve, JOB_POLL_MS));
      const next = await client
        .query(REQUEUE_ALL_JOB, { id: job.id }, { requestPolicy: "network-only" })
        .toPromise();
      if (next.error) break;
      job = next.data?.operations?.deadLetters.requeueAllJob;
    }
    setBusy(false);
    if (!job)
      toast(
        started?.awaitingAtStart != null
          ? `Requeuing ${started.awaitingAtStart} dead letter(s) on the server; the list refreshes as it goes.`
          : "Requeue all is running on the server; the list refreshes as it goes.",
        "info",
      );
    else if (job.status === "SUCCEEDED") {
      const c = job.count ?? 0;
      toast(
        c > 0 ? `${c} dead letter(s) requeued.` : "No unresolved dead letters to requeue.",
        c > 0 ? "success" : "info",
      );
    } else if (job.status === "RUNNING") toast(job.message, "info");
    else toast(job.message, "error");
    refetch();
  }

  async function onAckConfirm(note: string) {
    setBusy(true);
    if (ackTarget === "all") {
      const r = await acknowledgeAll({ note });
      setBusy(false);
      setAckTarget(null);
      if (r.error) toast(r.error.message, "error");
      else {
        const c = r.data?.operations.deadLetters.acknowledgeAllDeadLetters.count ?? 0;
        toast(
          c > 0
            ? `${c} dead letter(s) acknowledged.`
            : "No unresolved dead letters to acknowledge.",
          c > 0 ? "success" : "info",
        );
      }
    } else if (ackTarget === "bulk") {
      const r = await acknowledgeMany({ ids: selectedIds, note });
      setBusy(false);
      setAckTarget(null);
      if (r.error) toast(r.error.message, "error");
      else {
        const c = r.data?.operations.deadLetters.acknowledgeDeadLetters.count ?? 0;
        toast(
          c > 0 ? `${c} dead letter(s) acknowledged.` : "Nothing acknowledged.",
          c > 0 ? "success" : "info",
        );
      }
    } else if (ackTarget != null) {
      const id = ackTarget;
      const r = await acknowledge({ id, note });
      const res = r.data?.operations?.deadLetters?.acknowledgeDeadLetter;
      setBusy(false);
      setAckTarget(null);
      if (r.error) toast(r.error.message, "error");
      else if (res?.success) toast(`Dead letter #${id} acknowledged.`, "success");
      else toast(res?.message ?? "Could not acknowledge.", "error");
    }
    refetch();
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-4">
        <h1 className="text-2xl font-bold text-fg">Dead letters</h1>
        <div className="flex items-center gap-2">
          <button
            onClick={() => onRequeueAll(false)}
            disabled={busy}
            className="text-sm px-3 py-1 rounded-md border border-accent-line text-accent-fg hover:bg-accent-soft disabled:opacity-50"
          >
            Requeue all
          </button>
          <button
            onClick={() => onRequeueAll(true)}
            disabled={busy}
            title="Requeue every dead letter; each run asks the model afresh instead of replaying the failed run's decisions."
            className="text-sm px-3 py-1 rounded-md text-accent-fg hover:bg-accent-soft disabled:opacity-50"
          >
            Requeue all, ask afresh
          </button>
          <button
            onClick={() => setAckTarget("all")}
            disabled={busy}
            className="text-sm px-3 py-1 rounded-md border border-line-strong text-fg-2 hover:bg-hover disabled:opacity-50"
          >
            Acknowledge all
          </button>
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
              setStatus(e.target.value as DeadLetterStatus | "");
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
            onClick={() => onBulkRequeue(false)}
            disabled={busy}
            className="text-accent-fg font-medium hover:underline disabled:opacity-50"
          >
            Requeue selected
          </button>
          <button
            onClick={() => onBulkRequeue(true)}
            disabled={busy}
            title="Requeue the selected dead letters; each run asks the model afresh instead of replaying the failed run's decisions."
            className="text-accent-fg font-medium hover:underline disabled:opacity-50"
          >
            Requeue selected, ask afresh
          </button>
          <button
            onClick={() => setAckTarget("bulk")}
            disabled={busy}
            className="text-fg-2 font-medium hover:underline disabled:opacity-50"
          >
            Acknowledge selected
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
                  disabled={actionable.length === 0}
                  onChange={(e) =>
                    setMany(
                      actionable.map((d) => d.id),
                      e.target.checked,
                    )
                  }
                />
              </th>
              <th className="px-4 py-2 font-medium">Manifest</th>
              <th className="px-4 py-2 font-medium">Status</th>
              <th className="px-4 py-2 font-medium">Reason</th>
              <th className="px-4 py-2 font-medium">Retries</th>
              <th className="px-4 py-2 font-medium text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {page?.items.map((d) => (
              <tr key={d.id}>
                <td className="px-4 py-2">
                  {d.status === "AWAITING_INTERVENTION" && (
                    <input
                      type="checkbox"
                      aria-label={`Select #${d.id}`}
                      checked={selected.has(d.id)}
                      onChange={() => toggle(d.id)}
                    />
                  )}
                </td>
                <td className="px-4 py-2">
                  <Link
                    to={`/dead-letters/${d.id}`}
                    className="font-medium text-accent-fg hover:underline"
                  >
                    {d.manifestName}
                  </Link>
                </td>
                <td className="px-4 py-2">
                  <span
                    className={`text-xs px-2 py-0.5 rounded-full ${STATUS_STYLE[d.status]}`}
                  >
                    {label(d.status)}
                  </span>
                </td>
                <td className="px-4 py-2 text-fg-2 max-w-xs truncate" title={d.reason}>
                  {d.reason}
                </td>
                <td className="px-4 py-2 text-fg-2">{d.retryCountAtDeadLetter}</td>
                <td className="px-4 py-2 text-right whitespace-nowrap">
                  {d.status === "AWAITING_INTERVENTION" ? (
                    <>
                      <button
                        onClick={() => onRequeue(d.id)}
                        className="text-accent-fg hover:underline mr-3"
                      >
                        Requeue
                      </button>
                      <button
                        onClick={() => setAckTarget(d.id)}
                        className="text-fg-2 hover:underline"
                      >
                        Acknowledge
                      </button>
                    </>
                  ) : (
                    <span className="text-faint">—</span>
                  )}
                </td>
              </tr>
            ))}
            {page?.items.length === 0 && (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-center text-muted">
                  No dead letters.
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

      {ackTarget != null && (
        <NotePrompt
          title={
            ackTarget === "all"
              ? "Acknowledge every unresolved dead letter"
              : ackTarget === "bulk"
                ? `Acknowledge ${selectedIds.length} dead letter(s)`
                : `Acknowledge dead letter #${ackTarget}`
          }
          label="Resolution note (why is this being closed without retry?)"
          confirmLabel="Acknowledge"
          busy={busy}
          onConfirm={onAckConfirm}
          onClose={() => setAckTarget(null)}
        />
      )}
    </div>
  );
}

function label(s: DeadLetterStatus): string {
  return s
    .toLowerCase()
    .replace(/_/g, " ")
    .replace(/^\w/, (c) => c.toUpperCase());
}
