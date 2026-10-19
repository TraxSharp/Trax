import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useQuery } from "urql";
import { BULK_OPERATION, BULK_OPERATIONS } from "../graphql/queries";
import { TermHelp } from "../components/TermHelp";
import { enumLabel, formatTime } from "../lib/format";
import {
  BULK_STATUS_STYLE,
  attempted,
  bulkItemPath,
  bulkTitle,
  describeFilter,
  filterListPath,
  type BulkOperationDetail,
  type BulkOperationStatus,
  type BulkOperationSummary,
} from "../lib/bulkOperations";

// The bulk operations this host ran (api/0045): the one running, and those that finished in the
// last day. A running operation is read again until it stops; the server keeps no history beyond
// that, and another host behind the same address does not know this one's.

const POLL_MS = 1000;
const FAILURES_PAGE = 50;
const DOCS = "https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations";

/** Reads the query again after `ms` while `running`, once per answer, so a stopped one is not read again. */
function useFollow(running: boolean, answer: unknown, again: () => void, ms = POLL_MS) {
  // Each answer schedules the next read; the latest closure is the one called.
  const ref = useRef(again);
  useEffect(() => {
    ref.current = again;
  });
  useEffect(() => {
    if (!running) return;
    const t = setTimeout(() => ref.current(), ms);
    return () => clearTimeout(t);
  }, [running, answer, ms]);
}

function StatusPill({ status }: { status: BulkOperationStatus }) {
  return (
    <span className={`inline-block text-xs font-medium px-2 py-0.5 rounded-full ${BULK_STATUS_STYLE[status]}`}>
      {enumLabel(status)}
    </span>
  );
}

function Progress({ op, label }: { op: BulkOperationSummary; label: string }) {
  const n = attempted(op);
  const pct = op.total > 0 ? Math.round((n / op.total) * 100) : 100;
  const donePct = op.total > 0 ? (op.done / op.total) * 100 : 0;
  const failedPct = op.total > 0 ? (op.failed / op.total) * 100 : 0;
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={op.total}
      aria-valuenow={n}
      aria-valuetext={`${n.toLocaleString()} of ${op.total.toLocaleString()} attempted (${pct}%)`}
      className="h-2 w-full min-w-24 rounded-full bg-sunken overflow-hidden flex"
    >
      <div className="h-full bg-ok" style={{ width: `${donePct}%` }} />
      <div className="h-full bg-danger" style={{ width: `${failedPct}%` }} />
    </div>
  );
}

interface ListData {
  operations: { bulkOperations: BulkOperationSummary[] };
}

export function BulkOperationsPage() {
  const [result, reexecute] = useQuery<ListData>({ query: BULK_OPERATIONS });
  const operations = result.data?.operations?.bulkOperations ?? [];
  const running = operations.some((o) => o.status === "RUNNING");
  useFollow(running, result.data, () => reexecute({ requestPolicy: "network-only" }));

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-4">
        <div className="flex items-center gap-2">
          <h1 className="text-2xl font-bold text-fg">Bulk operations</h1>
          <TermHelp term="bulkOperation" />
        </div>
        <button
          onClick={() => reexecute({ requestPolicy: "network-only" })}
          className="text-sm px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 hover:bg-hover"
        >
          Refresh
        </button>
      </div>

      <p className="text-sm text-muted mb-4">
        One action over every row a list's filter matched when it started. This host keeps the one
        running and those that finished in the last day, at most 100.
      </p>

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium">Operation</th>
              <th className="px-4 py-2 font-medium">Status</th>
              <th className="px-4 py-2 font-medium w-48">Progress</th>
              <th className="px-4 py-2 font-medium text-right">Matched</th>
              <th className="px-4 py-2 font-medium text-right">Done</th>
              <th className="px-4 py-2 font-medium text-right">Failed</th>
              <th className="px-4 py-2 font-medium">Started by</th>
              <th className="px-4 py-2 font-medium">Started</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {operations.map((o) => (
              <tr key={o.id}>
                <td className="px-4 py-2">
                  <Link to={`/system/operations/${o.id}`} className="font-medium text-accent-fg hover:underline">
                    {bulkTitle(o.kind)}
                  </Link>
                </td>
                <td className="px-4 py-2">
                  <StatusPill status={o.status} />
                </td>
                <td className="px-4 py-2">
                  <Progress op={o} label={`Progress of ${bulkTitle(o.kind)}`} />
                </td>
                <td className="px-4 py-2 text-right tabular-nums">{o.total.toLocaleString()}</td>
                <td className="px-4 py-2 text-right tabular-nums text-ok-fg">{o.done.toLocaleString()}</td>
                <td className="px-4 py-2 text-right tabular-nums text-danger-fg">{o.failed.toLocaleString()}</td>
                <td className="px-4 py-2 text-fg-2 font-mono text-xs">{o.actor}</td>
                <td className="px-4 py-2 text-fg-2">{formatTime(o.startedAt)}</td>
              </tr>
            ))}
            {!result.fetching && operations.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-muted">
                  No bulk operation ran on this host in the last day. Start one from the batch bar of
                  Runs, Dead letters or Manifests: tick a row, then apply the action to all matching.{" "}
                  <a href={DOCS} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
                    How bulk operations work
                  </a>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

interface DetailData {
  operations: { bulkOperation: BulkOperationDetail | null };
}

export function BulkOperationPage() {
  const { id = "" } = useParams();
  // Keyset over the failures: a stack of afterId cursors, the first page null.
  const [cursors, setCursors] = useState<(number | null)[]>([null]);
  const failuresAfter = cursors[cursors.length - 1];
  const [result, reexecute] = useQuery<DetailData>({
    query: BULK_OPERATION,
    variables: { id, failuresAfter, failuresTake: FAILURES_PAGE },
  });
  const op = result.data?.operations?.bulkOperation;
  useFollow(op?.status === "RUNNING", result.data, () => reexecute({ requestPolicy: "network-only" }));

  const back = (
    <Link to="/system/operations" className="text-sm text-accent-fg hover:underline">
      All bulk operations
    </Link>
  );

  if (result.error)
    return (
      <div>
        {back}
        <div className="mt-4 bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm">
          {result.error.message}
        </div>
      </div>
    );
  if (!result.data) return <div>{back}</div>;
  if (!op)
    return (
      <div>
        {back}
        <h1 className="mt-4 text-2xl font-bold text-fg">Bulk operation</h1>
        <p className="mt-2 text-sm text-muted">
          This host does not know operation <span className="font-mono">{id}</span>. A host keeps a bulk
          operation for a day after it finishes, at most 100, and forgets them when it restarts; one
          started on another host is read there.
        </p>
      </div>
    );

  const filter = describeFilter(op.filter);
  const notAttempted = op.total - attempted(op);
  const failures = op.failures;
  const nextAfter = failures.length === FAILURES_PAGE ? failures[failures.length - 1].id : null;

  return (
    <div className="space-y-6">
      <div>
        {back}
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-bold text-fg">Bulk operation</h1>
          <TermHelp term="bulkOperation" />
          <StatusPill status={op.status} />
        </div>
        <p className="mt-1 text-sm text-fg-2">
          {bulkTitle(op.kind)}
          {op.askAfresh ? ", asking afresh" : ""} · started by{" "}
          <span className="font-mono text-xs">{op.actor}</span> · {formatTime(op.startedAt)}
          {op.finishedAt ? ` · stopped ${formatTime(op.finishedAt)}` : ""}
        </p>
      </div>

      <section aria-label="Counts" className="bg-surface rounded-lg border border-line p-5 space-y-4">
        <Progress op={op} label="Progress" />
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
          <Count label="Matched" value={op.total} />
          <Count label="Done" value={op.done} tone="text-ok-fg" />
          <Count label="Failed" value={op.failed} tone="text-danger-fg" />
          <Count label={op.status === "RUNNING" ? "Still to do" : "Not attempted"} value={notAttempted} />
        </div>
        <p className="text-sm text-fg-2">{op.message}</p>
      </section>

      <section aria-label="Filter" className="bg-surface rounded-lg border border-line p-5">
        <div className="flex items-center justify-between gap-4 mb-3">
          <h2 className="text-sm font-semibold text-fg">The filter it ran over</h2>
          <Link to={filterListPath(op.kind, op.filter)} className="text-sm text-accent-fg hover:underline">
            Open the list with this filter
          </Link>
        </div>
        {filter.length === 0 ? (
          <p className="text-sm text-muted">No filter: every row of the list when it started.</p>
        ) : (
          <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1 text-sm">
            {filter.map((f) => (
              <div key={f.label} className="contents">
                <dt className="text-muted">{f.label}</dt>
                <dd className="text-fg-2 break-all">{f.value}</dd>
              </div>
            ))}
          </dl>
        )}
      </section>

      <section aria-label="Failure reasons" className="bg-surface rounded-lg border border-line overflow-hidden">
        <h2 className="px-5 pt-4 pb-2 text-sm font-semibold text-fg">Why ids did not take</h2>
        {op.failureReasons.length === 0 ? (
          <p className="px-5 pb-4 text-sm text-muted">
            {op.failed === 0 ? "Every id attempted so far took." : "No reason was recorded."}
          </p>
        ) : (
          <table className="w-full text-sm">
            <thead className="bg-sunken text-muted text-left">
              <tr>
                <th className="px-5 py-2 font-medium">Reason</th>
                <th className="px-5 py-2 font-medium text-right">Ids</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {op.failureReasons.map((r) => (
                <tr key={r.reason}>
                  <td className="px-5 py-2 text-fg-2">{r.reason}</td>
                  <td className="px-5 py-2 text-right tabular-nums">{r.count.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {(op.failed > 0 || !isFirst(cursors)) && (
        <section aria-label="Failures" className="bg-surface rounded-lg border border-line overflow-hidden">
          <h2 className="px-5 pt-4 pb-2 text-sm font-semibold text-fg">Each id that did not take</h2>
          <table className="w-full text-sm">
            <thead className="bg-sunken text-muted text-left">
              <tr>
                <th className="px-5 py-2 font-medium">Id</th>
                <th className="px-5 py-2 font-medium">Reason</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {failures.map((f) => (
                <tr key={f.id}>
                  <td className="px-5 py-2">
                    <Link to={bulkItemPath(op.kind, f.id)} className="text-accent-fg hover:underline">
                      #{f.id}
                    </Link>
                  </td>
                  <td className="px-5 py-2 text-fg-2">{f.reason}</td>
                </tr>
              ))}
              {failures.length === 0 && (
                <tr>
                  <td colSpan={2} className="px-5 py-6 text-center text-muted">
                    No more failures kept with their id. Only the first 10,000 keep it; every one is counted
                    by reason above.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
          <div className="flex justify-end gap-2 px-5 py-3 text-sm">
            <button
              onClick={() => setCursors((c) => c.slice(0, -1))}
              disabled={isFirst(cursors)}
              className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
            >
              Previous
            </button>
            <button
              onClick={() => nextAfter != null && setCursors((c) => [...c, nextAfter])}
              disabled={nextAfter == null}
              className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
            >
              Next
            </button>
          </div>
        </section>
      )}
    </div>
  );
}

function isFirst(cursors: (number | null)[]): boolean {
  return cursors.length === 1;
}

// One count, named by its label so it can be found by it.
function Count({ label, value, tone = "text-fg" }: { label: string; value: number; tone?: string }) {
  return (
    <div role="group" aria-label={label}>
      <div className="text-muted">{label}</div>
      <div className={`text-xl font-semibold tabular-nums ${tone}`}>{value.toLocaleString()}</div>
    </div>
  );
}
