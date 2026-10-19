import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useClient, useMutation, useQuery } from "urql";
import { DEAD_LETTER_GROUPS, DEAD_LETTERS, REQUEUE_ALL_JOB } from "../graphql/queries";
import {
  ACKNOWLEDGE_ALL_DEAD_LETTERS,
  ACKNOWLEDGE_DEAD_LETTER,
  ACKNOWLEDGE_DEAD_LETTERS,
  REQUEUE_ALL_DEAD_LETTERS,
  REQUEUE_DEAD_LETTER,
  REQUEUE_DEAD_LETTERS,
} from "../graphql/mutations";
import { NotePrompt } from "../components/NotePrompt";
import { useKeyset } from "../lib/useKeyset";
import { useSelection } from "../lib/useSelection";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { toast } from "../lib/toast";
import type { DeadLetterStatus, DeadLetterSummary, PagedResult } from "../types";
import { confirmAction } from "../lib/confirm";
import { useUrlState } from "../lib/urlState";
import { TermHelp } from "../components/TermHelp";
import { ApplyToAllMatching } from "../components/ApplyToAllMatching";
import { offersDecisions, useTrainsAskingDecisions } from "../lib/decisions";
import { useAnswers } from "../lib/answerable";
import { enumLabel, formatTime, shortName } from "../lib/format";
import { RunsTabs } from "../components/RunsTabs";
import { groupDeadLetters, type DeadLetterGroup } from "../lib/deadLetterGroups";
import {
  BatchBar,
  EmptyState,
  ListView,
  MaybeIdLink,
  type Column,
  type ListGroup,
} from "../components/ListView";

const PAGE_SIZE = 25;
// How many of the newest dead letters the grouped view reads: the largest page the API serves.
// More than that and the page says so, and the list pages through every one.
const GROUP_WINDOW = 500;
const DOCS = "https://traxsharp.net/docs/scheduler/dead-letters-and-cleanup";
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
  const [status, setStatus] = useUrlState<DeadLetterStatus | "">("status");
  // One manifest's dead letters; the dead-letter page links here with ?manifestId=.
  const [manifestIdParam, setManifestId] = useUrlState("manifestId");
  const manifestId = manifestIdParam.replace(/\D/g, "");
  // Grouped by train and reason, or the plain list that pages through every dead letter. The demo
  // has no recording of the grouped read, so it shows the list (lib/answerable.ts).
  const groupable = useAnswers("DeadLetterGroups");
  const [viewParam, setView] = useUrlState<"groups" | "list">("view", "groups");
  // The Overview links one group (a train and the reason its dead letters share) by these two.
  const [trainFilter, setTrainFilter] = useUrlState("train");
  const [reasonFilter, setReasonFilter] = useUrlState("reason");
  const view = groupable ? viewParam : "list";
  const { selected, toggle, setMany, clear } = useSelection();
  // note dialog: single dead-letter id, "bulk" (selection), "all" (every unresolved), or null.
  const [ackTarget, setAckTarget] = useState<number | "bulk" | "all" | null>(null);
  const [busy, setBusy] = useState(false);
  // The groups whose rows are shown, beside the ones open by default (a group of one).
  const [toggled, setToggled] = useState<Set<string>>(new Set());

  const filters = { status: status || null, manifestId: manifestId ? Number(manifestId) : null };
  const [listResult, reexecuteList] = useQuery<DeadLettersData>({
    query: DEAD_LETTERS,
    variables: { take: PAGE_SIZE, afterId, ...filters },
    pause: view !== "list",
  });
  const [groupResult, reexecuteGroups] = useQuery<DeadLettersData>({
    query: DEAD_LETTER_GROUPS,
    variables: { take: GROUP_WINDOW, ...filters },
    pause: view !== "groups",
  });
  const result = view === "list" ? listResult : groupResult;
  const reexecute = view === "list" ? reexecuteList : reexecuteGroups;
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
  const items = useMemo(() => page?.items ?? [], [page]);
  const refetch = () => {
    clear();
    reexecute({ requestPolicy: "network-only" });
  };
  const onFilter = () => {
    clear();
    reset();
  };

  const selectedIds = [...selected];
  const askingDecisions = useTrainsAskingDecisions();
  const listedAsk = offersDecisions(askingDecisions, items.map((d) => d.manifestName));
  const selectedAsk = offersDecisions(
    askingDecisions,
    items.filter((d) => selected.has(d.id)).map((d) => d.manifestName),
  );

  const groups = useMemo(
    () =>
      groupDeadLetters(items).filter(
        (g) => (trainFilter === "" || g.train === trainFilter) && (reasonFilter === "" || g.reason === reasonFilter),
      ),
    [items, trainFilter, reasonFilter],
  );
  const narrowedToGroup = trainFilter !== "" || reasonFilter !== "";
  const hasFilter = status !== "" || manifestId !== "" || narrowedToGroup;

  async function onRequeue(id: number) {
    if (
      !(await confirmAction({
        title: "Requeue dead letter",
        message: `Requeue dead letter #${id}? A new run of its manifest is queued.`,
      }))
    )
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
      !(await confirmAction({
        title: askAfresh ? "Requeue, asking afresh" : "Requeue dead letters",
        message: askAfresh
          ? `Requeue ${selectedIds.length} dead letter(s), asking afresh?`
          : `Requeue ${selectedIds.length} dead letter(s)?`,
      }))
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
      !(await confirmAction({
        title: askAfresh ? "Requeue all, asking afresh" : "Requeue all dead letters",
        message: askAfresh
          ? "Requeue every unresolved dead letter, asking afresh? Each new run asks its decider again instead of replaying the failed run's decisions."
          : "Requeue every unresolved dead letter? A new run is queued for each.",
        danger: true,
        typeToConfirm: "all",
      }))
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

  const actions: Column<DeadLetterSummary> = {
    header: "Actions",
    cell: (d) =>
      d.status === "AWAITING_INTERVENTION" ? (
        <>
          <button onClick={() => onRequeue(d.id)} className="text-accent-fg hover:underline mr-3">
            Requeue
          </button>
          <button onClick={() => setAckTarget(d.id)} className="text-fg-2 hover:underline">
            Acknowledge
          </button>
        </>
      ) : (
        <span className="text-faint">—</span>
      ),
    className: "text-right whitespace-nowrap",
  };

  return (
    <div>
      <RunsTabs />
      <div className="flex flex-wrap items-center justify-between mb-6 gap-4">
        <div className="flex items-center gap-2">
          <h1 className="text-2xl font-bold text-fg">Dead letters</h1>
          <TermHelp term="deadLetter" />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={() => onRequeueAll(false)}
            disabled={busy}
            className="text-sm px-3 py-1 rounded-md border border-accent-line text-accent-fg hover:bg-accent-soft disabled:opacity-50"
          >
            Requeue all
          </button>
          {listedAsk && (
            <button
              onClick={() => onRequeueAll(true)}
              disabled={busy}
              title="Requeue every dead letter; each run asks the model afresh instead of replaying the failed run's decisions."
              className="text-sm px-3 py-1 rounded-md text-accent-fg hover:bg-accent-soft disabled:opacity-50"
            >
              Requeue all, ask afresh
            </button>
          )}
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
              onFilter();
            }}
            placeholder="Manifest id…"
            className="text-sm border border-line-strong rounded-md px-2 py-1 w-28"
          />
          <select
            aria-label="Status"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as DeadLetterStatus | "");
              onFilter();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {s === "" ? "All statuses" : enumLabel(s)}
              </option>
            ))}
          </select>
          {groupable && (
            <div role="group" aria-label="Show dead letters" className="flex rounded-md border border-line-strong overflow-hidden text-sm">
              {(
                [
                  ["groups", "By train and reason"],
                  ["list", "List"],
                ] as const
              ).map(([v, text]) => (
                <button
                  key={v}
                  aria-pressed={view === v}
                  onClick={() => {
                    setView(v);
                    onFilter();
                  }}
                  className={`px-2 py-1 ${view === v ? "bg-accent-soft text-accent-fg font-medium" : "text-fg-2 hover:bg-hover"}`}
                >
                  {text}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      <BatchBar count={selectedIds.length} onClear={clear}>
        <button
          onClick={() => onBulkRequeue(false)}
          disabled={busy}
          className="text-accent-fg font-medium hover:underline disabled:opacity-50"
        >
          Requeue selected
        </button>
        {selectedAsk && (
          <button
            onClick={() => onBulkRequeue(true)}
            disabled={busy}
            title="Requeue the selected dead letters; each run asks the model afresh instead of replaying the failed run's decisions."
            className="text-accent-fg font-medium hover:underline disabled:opacity-50"
          >
            Requeue selected, ask afresh
          </button>
        )}
        <button
          onClick={() => setAckTarget("bulk")}
          disabled={busy}
          className="text-fg-2 font-medium hover:underline disabled:opacity-50"
        >
          Acknowledge selected
        </button>
        <ApplyToAllMatching
          total={page?.totalCount}
          filter={{
            deadLetters: { status: status || null, manifestId: manifestId ? Number(manifestId) : null },
          }}
          unavailable={
            status === "AWAITING_INTERVENTION" ? undefined : "Show only Awaiting intervention to act on all matching."
          }
          actions={[
            { kind: "REQUEUE", explain: "A new run of each one's manifest is queued.", danger: false },
            { kind: "ACKNOWLEDGE", explain: "Each is closed with the note, without a retry." },
          ]}
        />
      </BatchBar>

      {view === "groups" && narrowedToGroup && (
        <p className="text-xs text-muted mb-2" role="status">
          Showing{trainFilter && <> {shortName(trainFilter)}</>}
          {reasonFilter && <>, failed with &ldquo;{reasonFilter}&rdquo;</>}.{" "}
          <button
            onClick={() => {
              setTrainFilter("");
              setReasonFilter("");
            }}
            className="text-accent-fg hover:underline"
          >
            Show every group
          </button>
        </p>
      )}

      {view === "groups" && page && page.totalCount > items.length && (
        <p className="text-xs text-muted mb-2">
          Grouped from the newest {items.length.toLocaleString()} of {page.totalCount.toLocaleString()} dead letters.
          Narrow by status or manifest, or{" "}
          <button onClick={() => setView("list")} className="text-accent-fg hover:underline">
            list every one
          </button>
          .
        </p>
      )}

      <ListView
        label="Dead letters"
        columns={view === "list" ? [...LIST_COLUMNS, actions] : [...GROUP_ROW_COLUMNS, actions]}
        rows={view === "list" ? page?.items : undefined}
        groups={
          view === "groups" && page
            ? groups.map(
                (g): ListGroup<DeadLetterSummary> => ({
                  key: g.key,
                  label: `${shortName(g.train)}, ${g.reason}`,
                  rows: g.rows,
                  summary: <GroupSummary group={g} />,
                  // A group of one shows its row; a bigger one opens on request.
                  expanded: (g.rows.length === 1 || narrowedToGroup) !== toggled.has(g.key),
                  onToggle: () =>
                    setToggled((t) => {
                      const n = new Set(t);
                      if (n.has(g.key)) n.delete(g.key);
                      else n.add(g.key);
                      return n;
                    }),
                }),
              )
            : undefined
        }
        selection={{ selected, toggle, setMany, can: (d) => d.status === "AWAITING_INTERVENTION" }}
        error={result.error?.message}
        empty={
          <EmptyState title="No dead letters." docs={DOCS}>
            {hasFilter
              ? "No dead letter matches these filters."
              : "A manifest's run is dead-lettered here once it has failed more times than its retries allow, and waits for someone to requeue or acknowledge it."}
          </EmptyState>
        }
        paging={
          view === "list"
            ? {
                total: page?.totalCount,
                isEstimated: page?.isEstimatedCount,
                isFirstPage,
                nextCursor: page?.nextCursor,
                onPrev: prev,
                onNext: next,
              }
            : undefined
        }
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

const MANIFESTS_SHOWN = 3;

function GroupSummary({ group: g }: { group: DeadLetterGroup }) {
  const n = g.rows.length;
  return (
    <div className="text-sm space-y-0.5">
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5">
        <Link
          to={`/trains/${encodeURIComponent(g.train)}`}
          title={g.train}
          className="font-medium text-accent-fg hover:underline"
        >
          {shortName(g.train)}
        </Link>
        <span className="text-fg-2 font-medium">
          {n} dead letter{n === 1 ? "" : "s"}
        </span>
        {g.awaiting !== n && <span className="text-xs text-muted">{g.awaiting} awaiting</span>}
        <span className="text-xs text-muted">latest {formatTime(g.latest)}</span>
      </div>
      <div className="text-xs text-muted">
        {g.manifests.length === 1 ? "Manifest " : `${g.manifests.length} manifests: `}
        {g.manifests.slice(0, MANIFESTS_SHOWN).map((m, i) => (
          <span key={m.id}>
            {i > 0 && ", "}
            <ManifestLink id={m.id} externalId={m.externalId} />
          </span>
        ))}
        {g.manifests.length > MANIFESTS_SHOWN && ` and ${g.manifests.length - MANIFESTS_SHOWN} more`}
      </div>
      <div className="text-xs text-danger-fg break-words line-clamp-2" title={g.reason}>
        {g.reason}
      </div>
    </div>
  );
}

/** A manifest by its own name (its external id) when the row carries it, by its id otherwise. */
function ManifestLink({ id, externalId }: { id: number; externalId: string | null | undefined }) {
  return (
    <Link to={`/schedules/${id}`} className="text-accent-fg hover:underline" title={`Manifest #${id}`}>
      {externalId ?? `#${id}`}
    </Link>
  );
}

function StatusBadge({ status }: { status: DeadLetterStatus }) {
  return <span className={`text-xs px-2 py-0.5 rounded-full ${STATUS_STYLE[status]}`}>{enumLabel(status)}</span>;
}

// A dead letter's row in the list: its train opens it, every id opens its page.
const LIST_COLUMNS: Column<DeadLetterSummary>[] = [
  {
    header: "Train",
    cell: (d) => (
      <Link to={`/runs/dead-letters/${d.id}`} title={d.manifestName} className="font-medium text-accent-fg hover:underline">
        {shortName(d.manifestName)}
      </Link>
    ),
  },
  { header: "Dead letter", cell: (d) => <MaybeIdLink to={(id) => `/runs/dead-letters/${id}`} id={d.id} />, className: "text-xs" },
  { header: "Manifest", cell: (d) => <ManifestLink id={d.manifestId} externalId={d.manifestExternalId} />, className: "text-xs" },
  { header: "Status", cell: (d) => <StatusBadge status={d.status} /> },
  {
    header: "Reason",
    cell: (d) => <span title={d.reason}>{d.reason}</span>,
    className: "text-fg-2 max-w-xs truncate",
  },
  { header: "Retries", cell: (d) => d.retryCountAtDeadLetter, className: "text-fg-2" },
  { header: "Dead-lettered", cell: (d) => formatTime(d.deadLetteredAt), className: "text-fg-2 whitespace-nowrap" },
];

// A row inside a group: the group already names its train and reason.
const GROUP_ROW_COLUMNS: Column<DeadLetterSummary>[] = [
  {
    header: "Dead letter",
    cell: (d) => <MaybeIdLink to={(id) => `/runs/dead-letters/${id}`} id={d.id} />,
    className: "text-xs",
  },
  { header: "Manifest", cell: (d) => <ManifestLink id={d.manifestId} externalId={d.manifestExternalId} />, className: "text-xs" },
  { header: "Status", cell: (d) => <StatusBadge status={d.status} /> },
  { header: "Retries", cell: (d) => d.retryCountAtDeadLetter, className: "text-fg-2" },
  { header: "Dead-lettered", cell: (d) => formatTime(d.deadLetteredAt), className: "text-fg-2 whitespace-nowrap" },
];
