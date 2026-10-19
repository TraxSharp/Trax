import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Pager } from "./Pager";

// The one list the Runs views are drawn with (All, Failed, Queued, Dead letters): the same table,
// selection, batch bar, empty state and keyset pager, so the four read as one area. Filters live
// in the URL (lib/urlState.ts); the page owns them and its query, the list draws what came back.

export interface Column<T> {
  /** The column's heading. */
  header: ReactNode;
  /** What one row shows in it. */
  cell: (row: T) => ReactNode;
  /** Classes for the column's cells (alignment, width, truncation). */
  className?: string;
  /** A hint on the heading, for a column whose name needs one. */
  title?: string;
}

export interface ListSelection<T> {
  selected: Set<number>;
  toggle: (id: number) => void;
  setMany: (ids: number[], on: boolean) => void;
  /** Whether a row can be acted on; only those get a checkbox. */
  can: (row: T) => boolean;
}

export interface ListPaging {
  total: number | undefined;
  isEstimated: boolean | undefined;
  isFirstPage: boolean;
  nextCursor: number | null | undefined;
  onPrev: () => void;
  onNext: (cursor: number) => void;
  /** A note beside the total ("live updates resume on page 1"). */
  note?: ReactNode;
}

/**
 * Rows that belong together (dead letters of one train that failed alike): a header row that
 * says what they share, with a checkbox for all of them, and the rows themselves while expanded.
 */
export interface ListGroup<T> {
  key: string;
  /** Names the group: its checkbox is "Select group: {label}", its toggle "Show {label}". */
  label: string;
  rows: T[];
  /** What the header row says beside the checkbox. */
  summary: ReactNode;
  expanded: boolean;
  onToggle: () => void;
}

export function ListView<T extends { id: number }>({
  label,
  columns,
  rows,
  groups,
  selection,
  empty,
  error,
  paging,
}: {
  /** The table's accessible name. */
  label: string;
  columns: Column<T>[];
  /** The rows, or undefined until the read answers. Ignored when `groups` is given. */
  rows?: T[] | undefined;
  /** The rows in groups, or undefined until the read answers. */
  groups?: ListGroup<T>[] | undefined;
  selection?: ListSelection<T>;
  /** Shown in place of the rows when the read answered with none. */
  empty: ReactNode;
  error?: string | null;
  paging?: ListPaging;
}) {
  const span = columns.length + (selection ? 1 : 0);
  const all = groups ? groups.flatMap((g) => g.rows) : rows;
  const answered = groups ?? rows;
  const row = (r: T, indent = false) => (
    <tr key={r.id}>
      {selection && (
        <td className={`py-2 ${indent ? "pl-8 pr-4" : "px-4"}`}>
          <RowCheckbox row={r} selection={selection} />
        </td>
      )}
      {columns.map((c, i) => (
        <td key={i} className={`px-4 py-2 ${c.className ?? ""}`}>
          {c.cell(r)}
        </td>
      ))}
    </tr>
  );
  return (
    <>
      {error && <ListError message={error} />}
      <div className="bg-surface rounded-lg border border-line overflow-x-auto">
        <table aria-label={label} className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              {selection && (
                <th className="px-4 py-2 w-8">
                  <SelectAll rows={all ?? []} selection={selection} />
                </th>
              )}
              {columns.map((c, i) => (
                <th key={i} title={c.title} className={`px-4 py-2 font-medium whitespace-nowrap ${c.className ?? ""}`}>
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {groups
              ? groups.map((g) => (
                  <GroupRows key={g.key} group={g} span={columns.length} selection={selection} row={row} />
                ))
              : rows?.map((r) => row(r))}
            {answered?.length === 0 && (
              <tr>
                <td colSpan={span} className="px-4 py-8">
                  {empty}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      {paging && (
        <Pager
          total={paging.total}
          isEstimated={paging.isEstimated}
          isFirstPage={paging.isFirstPage}
          nextCursor={paging.nextCursor}
          onPrev={paging.onPrev}
          onNext={paging.onNext}
          note={paging.note}
        />
      )}
    </>
  );
}

function GroupRows<T extends { id: number }>({
  group,
  span,
  selection,
  row,
}: {
  group: ListGroup<T>;
  span: number;
  selection?: ListSelection<T>;
  row: (r: T, indent: boolean) => ReactNode;
}) {
  return (
    <>
      <tr className="bg-sunken/60">
        {selection && (
          <td className="px-4 py-2 align-top">
            <SelectAll rows={group.rows} selection={selection} label={`Select group: ${group.label}`} />
          </td>
        )}
        <td colSpan={span} className="px-4 py-2">
          <div className="flex flex-wrap items-start gap-x-4 gap-y-1">
            <div className="min-w-0 flex-1">{group.summary}</div>
            <button
              onClick={group.onToggle}
              aria-expanded={group.expanded}
              aria-label={`${group.expanded ? "Hide" : "Show"} ${group.label}`}
              className="text-xs text-accent-fg hover:underline whitespace-nowrap"
            >
              {group.expanded ? "Hide" : `Show ${group.rows.length}`}
            </button>
          </div>
        </td>
      </tr>
      {group.expanded && group.rows.map((r) => row(r, true))}
    </>
  );
}

/** The header checkbox: every actionable row shown, on or off. */
export function SelectAll<T extends { id: number }>({
  rows,
  selection,
  label = "Select all",
}: {
  rows: T[];
  selection: ListSelection<T>;
  label?: string;
}) {
  const actionable = rows.filter(selection.can);
  const all = actionable.length > 0 && actionable.every((r) => selection.selected.has(r.id));
  return (
    <input
      type="checkbox"
      aria-label={label}
      checked={all}
      disabled={actionable.length === 0}
      onChange={(e) => selection.setMany(actionable.map((r) => r.id), e.target.checked)}
    />
  );
}

/** One row's checkbox, named `Select #id`, on the rows that can be acted on. */
export function RowCheckbox<T extends { id: number }>({ row, selection }: { row: T; selection: ListSelection<T> }) {
  if (!selection.can(row)) return null;
  return (
    <input
      type="checkbox"
      aria-label={`Select #${row.id}`}
      checked={selection.selected.has(row.id)}
      onChange={() => selection.toggle(row.id)}
    />
  );
}

/** What the selection can do: shown while anything is selected. */
export function BatchBar({ count, onClear, children }: { count: number; onClear: () => void; children: ReactNode }) {
  if (count === 0) return null;
  return (
    <div className="flex flex-wrap items-center gap-3 mb-3 px-4 py-2 rounded-lg bg-accent-soft border border-accent-line text-sm">
      <span className="text-accent-fg font-medium">{count} selected</span>
      {children}
      <button onClick={onClear} className="ml-auto text-muted hover:underline">
        Clear
      </button>
    </div>
  );
}

/**
 * An empty list: what it lists, what puts something in it, and where the docs explain it. The
 * first line is the list's own "No runs." so a glance reads it.
 */
export function EmptyState({ title, children, docs }: { title: string; children: ReactNode; docs: string }) {
  return (
    <div className="text-center text-muted space-y-1">
      <p className="font-medium text-fg-2">{title}</p>
      <p className="text-xs">
        {children}{" "}
        <a href={docs} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
          Read more
        </a>
      </p>
    </div>
  );
}

export function ListError({ message }: { message: string }) {
  return (
    <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">{message}</div>
  );
}

/** An id that opens its page: `#42`. */
export function IdLink({ to, id, title }: { to: string; id: number | string; title?: string }) {
  return (
    <Link to={to} title={title} className="text-accent-fg hover:underline tabular-nums">
      #{id}
    </Link>
  );
}

/** An optional id: its link, or a dash. */
export function MaybeIdLink({ to, id, title }: { to: (id: number) => string; id: number | null | undefined; title?: string }) {
  return id != null ? <IdLink to={to(id)} id={id} title={title} /> : <span className="text-faint">—</span>;
}
