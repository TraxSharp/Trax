import { useMemo, useState } from "react";
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
  // Keyset pagination: a stack of page-start cursors (afterId). The first page is null.
  // We never use offset `skip`, which scans every skipped row (see the API scaling notes).
  const [cursors, setCursors] = useState<(number | null)[]>([null]);
  const afterId = cursors[cursors.length - 1];
  const isFirstPage = cursors.length === 1;
  const [trainState, setTrainState] = useState<TrainState | "">("");
  const [trainName, setTrainName] = useState("");
  const [order, setOrder] = useState<"NEWEST" | "OLDEST">("NEWEST");
  const [failureClass, setFailureClass] = useState<FailureClass | "">("");
  const [after, setAfter] = useState(""); // datetime-local value
  const [before, setBefore] = useState("");
  // Exact-match filters the API serves from its own indexes: one run by external id, a run's
  // children, and what one machine ran.
  const [externalId, setExternalId] = useState("");
  const [parentId, setParentId] = useState("");
  const [hostName, setHostName] = useState("");
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
  const resetToFirst = () => setCursors([null]);
  const hideAdmin = useHideAdminTrains();

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

  // On the first page, overlay live events: prepend brand-new executions and update the
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
  const actionable = rows.filter((e) => ACTIVE_STATES.has(e.trainState));
  const selectedIds = [...selected];
  const allSelected =
    actionable.length > 0 && actionable.every((e) => selected.has(e.id));

  async function onBulkCancel() {
    if (selectedIds.length === 0) return;
    if (!confirm(`Request cancellation of ${selectedIds.length} execution(s)?`)) return;
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
            ? `Cancellation requested for ${n} execution(s).`
            : "None of the selected executions is still cancellable."),
        n > 0 ? "success" : "info",
      );
    } else toast(res?.message ?? "Could not request cancellation.", "error");
    clear();
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-4">
        <h1 className="text-2xl font-bold text-fg">Executions</h1>
        <div className="flex gap-2">
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
            value={trainState}
            onChange={(e) => {
              setTrainState(e.target.value as TrainState | "");
              resetToFirst();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            {STATES.map((s) => (
              <option key={s} value={s}>
                {s === "" ? "All states" : label(s)}
              </option>
            ))}
          </select>
          <select
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
            Hide admin trains
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
          title="Only the execution with this external id"
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
          title="Only the executions started from inside this execution's run"
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
          title="Only the executions run on the machine with this name"
          className="border border-line-strong rounded-md px-2 py-1 w-44 bg-field"
        />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2">
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
                className="text-danger-fg font-medium hover:underline"
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
                      disabled={actionable.length === 0}
                      onChange={(ev) =>
                        setMany(actionable.map((e) => e.id), ev.target.checked)
                      }
                    />
                  </th>
                  <th className="px-4 py-2 font-medium">Train</th>
                  <th className="px-4 py-2 font-medium">State</th>
                  <th className="px-4 py-2 font-medium">Started</th>
                  <th className="px-4 py-2 font-medium">Duration</th>
                  <th className="px-4 py-2 font-medium">Running junction</th>
                  <th className="px-4 py-2 font-medium">Parent</th>
                  <th className="px-4 py-2 font-medium">Failure class</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {rows.map((e) => (
                  <tr key={e.id}>
                    <td className="px-4 py-2">
                      {ACTIVE_STATES.has(e.trainState) && (
                        <input
                          type="checkbox"
                          aria-label={`Select #${e.id}`}
                          checked={selected.has(e.id)}
                          onChange={() => toggle(e.id)}
                        />
                      )}
                    </td>
                    <td className="px-4 py-2">
                      <Link
                        to={`/executions/${e.id}`}
                        className="font-medium text-accent-fg hover:underline"
                        title={e.name}
                      >
                        {shortName(e.name)}
                      </Link>
                    </td>
                    <td className="px-4 py-2">
                      <StateBadge state={e.trainState} />
                      {e.cancellationRequested && ACTIVE_STATES.has(e.trainState) && (
                        <span className="ml-1 text-[10px] text-warn-fg" title="Cancellation requested">
                          cancelling
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-2 text-fg-2">
                      {new Date(e.startTime).toLocaleString()}
                    </td>
                    <td className="px-4 py-2 text-fg-2">
                      {duration(e.startTime, e.endTime)}
                    </td>
                    <td className="px-4 py-2 text-fg-2 text-xs max-w-[10rem] truncate" title={e.currentlyRunningJunction ?? ""}>
                      {e.trainState === "IN_PROGRESS" && e.currentlyRunningJunction ? e.currentlyRunningJunction : ""}
                    </td>
                    <td className="px-4 py-2 text-xs">
                      {e.parentId != null && (
                        <Link to={`/executions/${e.parentId}`} className="text-accent-fg hover:underline">
                          #{e.parentId}
                        </Link>
                      )}
                    </td>
                    <td className="px-4 py-2 text-danger-fg text-xs">
                      {e.trainState === "FAILED" && e.failureClass ? enumLabel(e.failureClass) : ""}
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-8 text-center text-muted">
                      No executions.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          <div className="flex items-center justify-between mt-4 text-sm">
            <span className="text-muted">
              {page
                ? `${page.isEstimatedCount ? "~" : ""}${page.totalCount.toLocaleString()} total`
                : ""}
              {!isFirstPage && (
                <span className="ml-2 text-muted">
                  · live updates resume on page 1
                </span>
              )}
            </span>
            <div className="flex gap-2">
              <button
                onClick={() => setCursors((c) => c.slice(0, -1))}
                disabled={isFirstPage}
                className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
              >
                Previous
              </button>
              <button
                onClick={() =>
                  page?.nextCursor != null &&
                  setCursors((c) => [...c, page.nextCursor])
                }
                disabled={page?.nextCursor == null}
                className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
              >
                Next
              </button>
            </div>
          </div>
        </div>

        <LiveFeed events={events ?? []} />
      </div>
    </div>
  );
}

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

function duration(start: string, end: string | null): string {
  if (!end) return "—";
  const ms = new Date(end).getTime() - new Date(start).getTime();
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}
