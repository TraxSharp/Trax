import { useState } from "react";
import { useQuery } from "urql";
import { LOGS } from "../graphql/queries";
import { Pager } from "./Pager";
import { useKeyset } from "../lib/useKeyset";
import { usePoll } from "../lib/poll";
import { useDebouncedValue } from "../lib/useDebouncedValue";
import { shortName } from "../lib/format";
import type { LogEntry, LogLevel, PagedResult } from "../types";

const LEVELS: (LogLevel | "")[] = ["", "TRACE", "DEBUG", "INFORMATION", "WARNING", "ERROR", "CRITICAL"];

const LEVEL_STYLE: Record<LogLevel, string> = {
  TRACE: "text-muted",
  DEBUG: "text-muted",
  INFORMATION: "text-info-fg",
  WARNING: "text-warn-fg",
  ERROR: "text-danger-fg",
  CRITICAL: "text-danger-fg font-bold",
  NONE: "text-muted",
};

const INPUT = "text-sm border border-line-strong rounded-md px-2 py-1 bg-field";

interface LogsData {
  operations: { logs: { logs: PagedResult<LogEntry> } };
}

export type LogOrder = "NEWEST" | "OLDEST";

/**
 * A keyset-paged grid of log entries with the filters operations.logs serves, as the Blazor
 * LogsGrid: run, minimum level, category contains and message contains, read newest or oldest
 * first. The filters sit above the grid, so nothing offers a filter the read ignores; the exception
 * text is not searchable. A text filter counts at most 10,000 matches, and then the pager says
 * "10,000+" and a caption says how to reach the rest.
 *
 * With `metadataId` it shows one run's entries and leaves out the run filter and column.
 */
export function LogsGrid({
  metadataId,
  defaultOrder = "NEWEST",
  pageSize = 50,
  poll = false,
}: {
  metadataId?: number;
  // A run's own log reads top to bottom, oldest first.
  defaultOrder?: LogOrder;
  pageSize?: number;
  // Refetch on the auto-refresh interval (the Logs page does; a run page reads once).
  poll?: boolean;
}) {
  const { afterId, isFirstPage, next, prev, reset } = useKeyset();
  const [runId, setRunId] = useState("");
  const [level, setLevel] = useState<LogLevel | "">("");
  const [category, setCategory] = useState("");
  const [message, setMessage] = useState("");
  const [order, setOrder] = useState<LogOrder>(defaultOrder);
  const categoryContains = useDebouncedValue(category.trim());
  const messageContains = useDebouncedValue(message.trim());
  const fixedRun = metadataId != null;

  const [result, reexecute] = useQuery<LogsData>({
    query: LOGS,
    variables: {
      take: pageSize,
      afterId,
      minimumLevel: level || null,
      categoryContains: categoryContains || null,
      messageContains: messageContains || null,
      metadataId: fixedRun ? metadataId : runId ? Number(runId) : null,
      order,
    },
  });
  usePoll(() => {
    if (poll) reexecute({ requestPolicy: "network-only" });
  });

  const page = result.data?.operations?.logs?.logs;
  const rows = page?.items ?? [];
  const columns = fixedRun ? 3 : 4;
  // A changed filter starts again from the first page.
  const change = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    reset();
  };

  return (
    <div>
      <div className="flex flex-wrap items-end gap-3 mb-2 text-sm">
        {!fixedRun && (
          <label className="flex flex-col gap-1 text-xs text-muted">
            Run (execution id)
            <input
              aria-label="Run"
              value={runId}
              onChange={(e) => change(setRunId)(e.target.value.replace(/\D/g, ""))}
              placeholder="Any run"
              className={`${INPUT} w-32`}
            />
          </label>
        )}
        <label className="flex flex-col gap-1 text-xs text-muted">
          Minimum level
          <select
            aria-label="Minimum level"
            value={level}
            onChange={(e) => change(setLevel)(e.target.value as LogLevel | "")}
            className={INPUT}
          >
            {LEVELS.map((l) => (
              <option key={l} value={l}>
                {l === "" ? "Any level" : levelLabel(l)}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted">
          Category contains
          <input
            aria-label="Category contains"
            value={category}
            onChange={(e) => change(setCategory)(e.target.value)}
            placeholder="Any category"
            className={`${INPUT} w-48`}
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted">
          Message contains
          <input
            aria-label="Message contains"
            value={message}
            onChange={(e) => change(setMessage)(e.target.value)}
            placeholder="Any message"
            className={`${INPUT} w-64`}
          />
        </label>
        <label className="flex flex-col gap-1 text-xs text-muted">
          Order
          <select
            aria-label="Order"
            value={order}
            onChange={(e) => change(setOrder)(e.target.value as LogOrder)}
            className={INPUT}
          >
            <option value="NEWEST">Newest first</option>
            <option value="OLDEST">Oldest first</option>
          </select>
        </label>
      </div>
      <p className="text-xs text-muted mb-2">
        Text filters ignore case. Entries are read in id order ({order === "OLDEST" ? "oldest" : "newest"} first);
        the exception text is not searchable.
      </p>
      {page?.isCountCapped && (
        <p role="status" className="text-xs text-warn-fg mb-2">
          More than {page.totalCount.toLocaleString("en-US")} entries match this text filter, so the count stops
          there. Narrow the filter, or filter by run or level, to reach the rest.
        </p>
      )}

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm" aria-label="Log entries">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium w-24">Level</th>
              <th className="px-4 py-2 font-medium">Message</th>
              <th className="px-4 py-2 font-medium w-48">Category</th>
              {!fixedRun && <th className="px-4 py-2 font-medium w-24">Run</th>}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map((l) => (
              <tr key={l.id}>
                <td className={`px-4 py-2 font-medium ${LEVEL_STYLE[l.level] ?? ""}`}>{levelLabel(l.level)}</td>
                <td className="px-4 py-2 text-fg">
                  <span className="block truncate max-w-xl" title={l.message}>
                    {l.message}
                  </span>
                  {l.exception && (
                    <span
                      className="block text-xs text-danger-fg truncate max-w-xl"
                      title={l.exception}
                    >
                      {l.exception}
                    </span>
                  )}
                </td>
                <td className="px-4 py-2 text-muted truncate" title={l.category}>
                  {shortName(l.category)}
                </td>
                {!fixedRun && (
                  <td className="px-4 py-2 text-muted">{l.metadataId || "—"}</td>
                )}
              </tr>
            ))}
            {page && rows.length === 0 && (
              <tr>
                <td colSpan={columns} className="px-4 py-8 text-center text-muted">
                  No logs.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <Pager
        total={page?.totalCount}
        isEstimated={page?.isEstimatedCount}
        isCapped={page?.isCountCapped}
        isFirstPage={isFirstPage}
        nextCursor={page?.nextCursor}
        onPrev={prev}
        onNext={next}
      />
    </div>
  );
}

function levelLabel(s: string): string {
  return s.charAt(0) + s.slice(1).toLowerCase();
}
