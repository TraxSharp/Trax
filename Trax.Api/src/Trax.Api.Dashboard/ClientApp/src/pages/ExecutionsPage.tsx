import { useMemo } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useSubscription } from "urql";
import { EXECUTIONS, ADMIN_TRAIN_NAMES } from "../graphql/queries";
import { CANCEL_EXECUTIONS } from "../graphql/mutations";
import { ON_TRAIN_STATE_CHANGED } from "../graphql/subscriptions";
import { StateBadge } from "../components/StateBadge";
import { useSelection } from "../lib/useSelection";
import { useHideAdminTrains, setHideAdminTrains } from "../lib/adminTrains";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { useRefetchOnReconnect } from "../lib/useRefetchOnReconnect";
import { toast } from "../lib/toast";
import { enumLabel } from "../lib/format";
import type {
  ExecutionSummary,
  FailureClass,
  PagedResult,
  TrainLifecycleEvent,
  TrainState,
} from "../types";
import { confirmAction } from "../lib/confirm";
import { useUrlState } from "../lib/urlState";
import { TermHelp } from "../components/TermHelp";
import { RunsTabs } from "../components/RunsTabs";
import { ApplyToAllMatching } from "../components/ApplyToAllMatching";
import { BatchBar, EmptyState, ListView, MaybeIdLink, type Column } from "../components/ListView";
import { useKeyset } from "../lib/useKeyset";

const ACTIVE_STATES = new Set<TrainState>(["PENDING", "IN_PROGRESS"]);

const PAGE_SIZE = 25;
const FEED_LIMIT = 100;
const FAILURE_CLASSES: (FailureClass | "")[] = [
  "",
  "TRANSIENT",
  "CONFLICT",
  "PERMANENT",
  "UNCLASSIFIED",
];
const STATES: (TrainState | "")[] = [
  "",
  "PENDING",
  "IN_PROGRESS",
  "COMPLETED",
  "FAILED",
  "CANCELLED",
];

interface ExecutionsData {
  operations: { executions: PagedResult<ExecutionSummary> };
}

export function ExecutionsPage() {
  // All and Failed are two views (components/RunsTabs.tsx): moving between them starts the other
  // on its first page with nothing selected, as opening a different list would.
  const [trainState] = useUrlState("state");
  return <RunsList key={trainState === "FAILED" ? "failed" : "all"} />;
}

function RunsList() {
  // Keyset pagination: a stack of page-start cursors (afterId). The first page is null.
  // We never use offset `skip`, which scans every skipped row (see the API scaling notes).
  const { afterId, isFirstPage, next, prev, reset: resetToFirst } = useKeyset();
  const [trainState, setTrainState] = useUrlState<TrainState | "">("state");
  const [trainName, setTrainName] = useUrlState("train");
  const [order, setOrder] = useUrlState<"NEWEST" | "OLDEST">("order", "NEWEST");
  const [failureClass, setFailureClass] = useUrlState<FailureClass | "">("failure");
  const [after, setAfter] = useUrlState("after"); // datetime-local value
  const [before, setBefore] = useUrlState("before");
  // Exact-match filters the API serves from its own indexes: one run by external id, a run's
  // children, and what one machine ran.
  const [externalId, setExternalId] = useUrlState("externalId");
  const [parentId, setParentId] = useUrlState("parentId");
  const [hostName, setHostName] = useUrlState("host");
  const hasFilter =
    trainState !== "" ||
    trainName !== "" ||
    order !== "NEWEST" ||
    failureClass !== "" ||
    after !== "" ||
    before !== "" ||
    externalId !== "" ||
    parentId !== "" ||
    hostName !== "";
  // The Failed view is this list filtered to state FAILED (components/RunsTabs.tsx).
  const failedView = trainState === "FAILED";
  // A filter that names a train asks for that train's runs, a scheduler train's included, so the
  // "hide scheduler trains" preference does not empty it.
  const hideAdmin = useHideAdminTrains() && trainName === "";

  const [result, reexecuteExecutions] = useQuery<ExecutionsData>({
    query: EXECUTIONS,
    variables: {
      take: PAGE_SIZE,
      afterId,
      trainState: trainState || null,
      trainName: trainName || null,
      order,
      startedAfter: after ? new Date(after).toISOString() : null,
      startedBefore: before ? new Date(before).toISOString() : null,
      hideAdminTrains: hideAdmin,
      failureClass: failureClass || null,
      externalId: externalId.trim() || null,
      parentId: parentId ? Number(parentId) : null,
      hostName: hostName.trim() || null,
    },
  });
  // The live feed re-subscribes on reconnect but misses events during the gap; refetch the base
  // grid so it reflects anything that happened while the socket was down.
  useRefetchOnReconnect(() => reexecuteExecutions({ requestPolicy: "network-only" }));
  // A cancel request sets cancellationRequested without a lifecycle event, so the feed never sees
  // it; the EXECUTION change signal does.
  useRefetchOnChange("EXECUTION", () => reexecuteExecutions({ requestPolicy: "network-only" }));
  const page = result.data?.operations?.executions;
  const { selected, toggle, setMany, clear } = useSelection();
  const [, cancelExecutions] = useMutation(CANCEL_EXECUTIONS);

  // Canonical FullNames of the admin trains, so the live feed can be filtered by the same rule the
  // server applies to the grid (the subscription streams every train on an admin host).
  const [adminNamesResult] = useQuery<{ operations: { adminTrainNames: string[] } }>({
    query: ADMIN_TRAIN_NAMES,
  });
  const adminNames = useMemo(
    () => new Set(adminNamesResult.data?.operations?.adminTrainNames ?? []),
    [adminNamesResult.data],
  );

  // One subscription drives both the live feed and the in-place table merge. It accumulates every
  // train; admin filtering happens at render time so toggling the setting re-filters instantly.
  const [{ data: allEvents }] = useSubscription<
    { onTrainStateChanged: TrainLifecycleEvent },
    TrainLifecycleEvent[]
  >({ query: ON_TRAIN_STATE_CHANGED }, (prev = [], data) =>
    [data.onTrainStateChanged, ...prev].slice(0, FEED_LIMIT),
  );
  const events = useMemo(
    () =>
      hideAdmin
        ? (allEvents ?? []).filter((e) => !adminNames.has(e.trainName))
        : (allEvents ?? []),
    [allEvents, hideAdmin, adminNames],
  );

  // On the first page, overlay live events: prepend brand-new runs and update the
  // state of rows already shown. Deeper pages show history unchanged (dedup by id means a
  // prepended row drops out once the base query refetches to include it).
  const rows = useMemo(() => {
    const base = page?.items ?? [];
    // Live-merge only on an unfiltered first page; otherwise a new event could inject a row
    // that doesn't match the active filter.
    if (!isFirstPage || hasFilter || !events?.length) return base;

    const baseIds = new Set(base.map((r) => r.id));
    const latestByMeta = new Map<number, TrainLifecycleEvent>();
    for (const e of events)
      if (!latestByMeta.has(e.metadataId)) latestByMeta.set(e.metadataId, e); // newest first

    const prepends: ExecutionSummary[] = [];
    const seen = new Set<number>();
    for (const e of events) {
      if (baseIds.has(e.metadataId) || seen.has(e.metadataId)) continue;
      seen.add(e.metadataId);
      prepends.push(eventToRow(e));
    }

    const patchedBase = base.map((row) => {
      const e = latestByMeta.get(row.id);
      return e ? { ...row, trainState: e.trainState } : row;
    });

    // Keep the first page the same length as any other page: live prepends push the oldest base
    // rows off the bottom rather than growing the grid without bound. The dropped rows reappear on
    // page 2, and the next base refetch folds the prepends into the real page.
    return [...prepends, ...patchedBase].slice(0, PAGE_SIZE);
  }, [page, events, isFirstPage, hasFilter]);

  // Only PENDING / IN_PROGRESS rows can be cancelled; the selection goes in one cancelExecutions call.
  const selectedIds = [...selected];

  async function onBulkCancel() {
    if (selectedIds.length === 0) return;
    if (
      !(await confirmAction({
        title: "Cancel runs",
        message: `Request cancellation of ${selectedIds.length} run(s)? A queued run never starts; a running one stops at its next junction.`,
        danger: true,
      }))
    )
      return;
    const r = await cancelExecutions({ ids: selectedIds });
    const res = r.data?.operations?.cancelExecutions;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) {
      // The API's message counts what it flagged and says why it skipped any (a run a user's
      // state-machine draft started is read-only to operators).
      const n = res.count ?? 0;
      toast(
        res.message ??
          (n > 0
            ? `Cancellation requested for ${n} run(s).`
            : "None of the selected runs is still cancellable."),
        n > 0 ? "success" : "info",
      );
    } else toast(res?.message ?? "Could not request cancellation.", "error");
    clear();
  }

  const columns = failedView ? [...RUN_COLUMNS.slice(0, 5), ...FAILED_COLUMNS, ...RUN_COLUMNS.slice(6)] : RUN_COLUMNS;

  return (
    <div>
      <RunsTabs />
      <div className="flex flex-wrap items-center justify-between mb-6 gap-4">
        <div className="flex items-center gap-2">
          <h1 className="text-2xl font-bold text-fg">{failedView ? "Failed runs" : "Runs"}</h1>
          <TermHelp term="run" />
        </div>
        <div className="flex flex-wrap gap-2">
          <input
            value={trainName}
            onChange={(e) => {
              setTrainName(e.target.value);
              resetToFirst();
            }}
            placeholder="Filter by train name…"
            className="text-sm border border-line-strong rounded-md px-2 py-1 w-64"
          />
          <select
            aria-label="State"
            value={trainState}
            onChange={(e) => {
              setTrainState(e.target.value as TrainState | "");
              resetToFirst();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            {STATES.map((s) => (
              <option key={s} value={s}>
                {s === "" ? "All states" : enumLabel(s)}
              </option>
            ))}
          </select>
          <select
            aria-label="Order"
            value={order}
            onChange={(e) => {
              setOrder(e.target.value as "NEWEST" | "OLDEST");
              resetToFirst();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            <option value="NEWEST">Newest first</option>
            <option value="OLDEST">Oldest first</option>
          </select>
          <select
            aria-label="Failure class"
            value={failureClass}
            onChange={(e) => {
              setFailureClass(e.target.value as FailureClass | "");
              resetToFirst();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            {FAILURE_CLASSES.map((f) => (
              <option key={f} value={f}>
                {f === "" ? "Any failure class" : enumLabel(f)}
              </option>
            ))}
          </select>
          <label
            className="flex items-center gap-2 text-sm text-fg-2 whitespace-nowrap"
            title="Hide the internal scheduler trains (JobDispatcher, ManifestManager, JobRunner, cleanup)"
          >
            <input
              type="checkbox"
              checked={hideAdmin}
              onChange={(e) => {
                setHideAdminTrains(e.target.checked);
                resetToFirst();
              }}
            />
            Hide scheduler trains
          </label>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-3 mb-4 text-sm">
        <label className="flex items-center gap-2 text-muted">
          After
          <input
            type="datetime-local"
            value={after}
            onChange={(e) => {
              setAfter(e.target.value);
              resetToFirst();
            }}
            className="border border-line-strong rounded-md px-2 py-1 bg-field"
          />
        </label>
        <label className="flex items-center gap-2 text-muted">
          Before
          <input
            type="datetime-local"
            value={before}
            onChange={(e) => {
              setBefore(e.target.value);
              resetToFirst();
            }}
            className="border border-line-strong rounded-md px-2 py-1 bg-field"
          />
        </label>
        {(after || before) && (
          <button
            onClick={() => {
              setAfter("");
              setBefore("");
              resetToFirst();
            }}
            className="text-muted hover:underline"
          >
            Clear range
          </button>
        )}
        <input
          aria-label="External id"
          value={externalId}
          onChange={(e) => {
            setExternalId(e.target.value);
            resetToFirst();
          }}
          placeholder="External id (exact)…"
          title="Only the run with this external id"
          className="border border-line-strong rounded-md px-2 py-1 w-56 bg-field"
        />
        <input
          aria-label="Parent id"
          value={parentId}
          onChange={(e) => {
            setParentId(e.target.value.replace(/\D/g, ""));
            resetToFirst();
          }}
          placeholder="Parent id…"
          title="Only the runs this run started"
          className="border border-line-strong rounded-md px-2 py-1 w-28 bg-field"
        />
        <input
          aria-label="Host name"
          value={hostName}
          onChange={(e) => {
            setHostName(e.target.value);
            resetToFirst();
          }}
          placeholder="Host name (exact)…"
          title="Only the runs on the host with this name"
          className="border border-line-strong rounded-md px-2 py-1 w-44 bg-field"
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2 min-w-0">
          <BatchBar count={selectedIds.length} onClear={clear}>
            <button onClick={onBulkCancel} className="text-danger-fg font-medium hover:underline">
              Cancel selected
            </button>
            <ApplyToAllMatching
              total={page?.totalCount}
              // The list read's filter, as its variables send it.
              filter={{
                executions: {
                  trainState: trainState || null,
                  trainName: trainName || null,
                  startedAfter: after ? new Date(after).toISOString() : null,
                  startedBefore: before ? new Date(before).toISOString() : null,
                  hideAdminTrains: hideAdmin,
                  failureClass: failureClass || null,
                  externalId: externalId.trim() || null,
                  parentId: parentId ? Number(parentId) : null,
                  hostName: hostName.trim() || null,
                },
              }}
              unavailable={
                ACTIVE_STATES.has(trainState as TrainState)
                  ? undefined
                  : "Show only Pending or In progress runs to cancel all matching."
              }
              actions={[
                {
                  kind: "CANCEL",
                  explain: "A queued run never starts; a running one stops at its next junction.",
                  danger: true,
                },
              ]}
            />
          </BatchBar>

          <ListView
            label={failedView ? "Failed runs" : "Runs"}
            columns={columns}
            rows={result.data || result.error ? rows : undefined}
            selection={{ selected, toggle, setMany, can: (e) => ACTIVE_STATES.has(e.trainState) }}
            error={result.error?.message}
            empty={
              hasFilter ? (
                <EmptyState title="No runs." docs={RUNS_DOCS}>
                  No run matches these filters.
                </EmptyState>
              ) : (
                <EmptyState title="No runs." docs={RUNS_DOCS}>
                  A run is listed here once a train is run or queued, by a schedule, a machine or a request.
                </EmptyState>
              )
            }
            paging={{
              total: page?.totalCount,
              isEstimated: page?.isEstimatedCount,
              isFirstPage,
              nextCursor: page?.nextCursor,
              onPrev: prev,
              onNext: next,
              note: !isFirstPage ? "· live updates resume on page 1" : undefined,
            }}
          />
        </div>

        <LiveFeed events={events ?? []} />
      </div>
    </div>
  );
}

const RUNS_DOCS = "https://traxsharp.net/docs/getting-started";

// Every Runs list shows a row the same way: its train links the run, every id opens its page.
const RUN_COLUMNS: Column<ExecutionSummary>[] = [
  {
    header: "Train",
    cell: (e) => (
      <Link to={`/runs/${e.id}`} className="font-medium text-accent-fg hover:underline" title={e.name}>
        {shortName(e.name)}
      </Link>
    ),
  },
  { header: "Run", cell: (e) => <MaybeIdLink to={(id) => `/runs/${id}`} id={e.id} />, className: "text-xs" },
  {
    header: "State",
    cell: (e) => (
      <>
        <StateBadge state={e.trainState} />
        {e.cancellationRequested && ACTIVE_STATES.has(e.trainState) && (
          <span className="ml-1 text-[10px] text-warn-fg" title="Cancellation requested">
            cancelling
          </span>
        )}
      </>
    ),
  },
  { header: "Started", cell: (e) => new Date(e.startTime).toLocaleString(), className: "text-fg-2 whitespace-nowrap" },
  { header: "Duration", cell: (e) => duration(e.startTime, e.endTime), className: "text-fg-2 whitespace-nowrap" },
  {
    header: "Running junction",
    cell: (e) => (e.trainState === "IN_PROGRESS" && e.currentlyRunningJunction ? e.currentlyRunningJunction : ""),
    className: "text-fg-2 text-xs max-w-[10rem] truncate",
  },
  {
    header: "Manifest",
    title: "The manifest that scheduled the run",
    cell: (e) => <MaybeIdLink to={(id) => `/schedules/${id}`} id={e.manifestId} />,
    className: "text-xs",
  },
  {
    header: "Parent",
    title: "The run that started this one",
    cell: (e) => (e.parentId != null ? <MaybeIdLink to={(id) => `/runs/${id}`} id={e.parentId} /> : null),
    className: "text-xs",
  },
  {
    header: "Failure class",
    cell: (e) => (e.trainState === "FAILED" && e.failureClass ? enumLabel(e.failureClass) : ""),
    className: "text-danger-fg text-xs",
  },
];

// The Failed view puts where and why a run failed in place of the running junction.
const FAILED_COLUMNS: Column<ExecutionSummary>[] = [
  { header: "Failed at", cell: (e) => e.failureJunction ?? "—", className: "text-fg-2 text-xs max-w-[10rem] truncate" },
  {
    header: "Reason",
    cell: (e) => <span title={e.failureReason ?? ""}>{e.failureReason ?? "—"}</span>,
    className: "text-fg-2 text-xs max-w-[14rem] truncate",
  },
];

function LiveFeed({ events }: { events: TrainLifecycleEvent[] }) {
  return (
    <div className="bg-surface rounded-lg border border-line p-5 h-fit lg:sticky lg:top-6 flex flex-col max-h-[calc(100vh-6rem)]">
      <div className="flex items-center gap-2 mb-3">
        <span className="relative flex h-2 w-2">
          <span className="absolute inline-flex h-full w-full rounded-full bg-ok opacity-75 animate-ping" />
          <span className="relative inline-flex rounded-full h-2 w-2 bg-ok" />
        </span>
        <h2 className="text-sm font-semibold text-fg">Live feed</h2>
      </div>

      {events.length === 0 && (
        <p className="text-sm text-muted">Waiting for events…</p>
      )}

      <ul className="space-y-2 overflow-y-auto min-h-0 pr-1">
        {events.map((e, i) => (
          <li key={`${e.metadataId}-${e.timestamp}-${i}`} className="text-sm">
            <div className="flex items-center justify-between gap-2">
              <span className="text-fg-2 truncate" title={e.trainName}>
                {shortName(e.trainName)}
              </span>
              <StateBadge state={e.trainState} />
            </div>
            <span className="text-xs text-muted">
              {new Date(e.timestamp).toLocaleTimeString()}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function eventToRow(e: TrainLifecycleEvent): ExecutionSummary {
  return {
    id: e.metadataId,
    externalId: e.externalId,
    name: e.trainName,
    trainState: e.trainState,
    startTime: e.timestamp,
    endTime: null,
    failureJunction: e.failureJunction,
    failureReason: e.failureReason,
    manifestId: null,
    hostName: null,
  };
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}

function duration(start: string, end: string | null): string {
  if (!end) return "—";
  const ms = new Date(end).getTime() - new Date(start).getTime();
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}
