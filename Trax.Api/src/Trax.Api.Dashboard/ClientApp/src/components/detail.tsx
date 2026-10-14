import { Link } from "react-router-dom";
import { enumLabel, prettyJson } from "../lib/format";
import type { FailureClass } from "../types";

export function BackLink({ to, label }: { to: string; label: string }) {
  return (
    <Link
      to={to}
      className="text-sm text-accent-fg hover:underline"
    >
      ← {label}
    </Link>
  );
}

export function DetailPanel({
  title,
  children,
}: {
  title?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="bg-surface rounded-lg border border-line p-5 mb-6">
      {title && (
        <h2 className="text-sm font-semibold text-fg mb-4">
          {title}
        </h2>
      )}
      {children}
    </div>
  );
}

export function Fields({ rows }: { rows: [string, React.ReactNode][] }) {
  return (
    <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-2 text-sm">
      {rows.map(([label, value]) => (
        <div key={label} className="flex justify-between gap-4 py-1">
          <dt className="text-muted shrink-0">{label}</dt>
          <dd className="text-fg font-medium text-right break-all">
            {value ?? "—"}
          </dd>
        </div>
      ))}
    </dl>
  );
}

export function ExceptionViewer({
  reason,
  junction,
  stackTrace,
  failureClass,
}: {
  reason: string | null;
  junction?: string | null;
  stackTrace?: string | null;
  // How the failure was classified (Transient, Conflict, Permanent, Unclassified).
  failureClass?: FailureClass | null;
}) {
  if (!reason && !stackTrace) return null;
  return (
    <div className="bg-danger-soft border border-danger-line rounded-lg p-5 mb-6">
      <h2 className="text-sm font-semibold text-danger-fg mb-2 flex items-center gap-2">
        Failure{junction ? ` at ${junction}` : ""}
        {failureClass && (
          <span
            title="Failure class"
            className="text-xs font-medium px-2 py-0.5 rounded-full bg-danger-soft text-danger-fg"
          >
            {enumLabel(failureClass)}
          </span>
        )}
      </h2>
      {reason && (
        <p className="text-sm text-danger-fg mb-2">{reason}</p>
      )}
      {stackTrace && (
        <pre className="text-xs text-danger-fg whitespace-pre-wrap overflow-auto max-h-64">
          {stackTrace}
        </pre>
      )}
    </div>
  );
}

export function Loading() {
  return <p className="text-muted">Loading…</p>;
}

export function NotFound({ what }: { what: string }) {
  return <p className="text-muted">No {what} found.</p>;
}

/** A titled panel showing JSON (pretty-printed when it parses). Renders nothing when empty. */
export function JsonPanel({ title, json }: { title: string; json: string | null | undefined }) {
  if (!json || !json.trim()) return null;
  return (
    <DetailPanel title={title}>
      <pre className="text-xs text-fg whitespace-pre-wrap break-all overflow-auto max-h-72 font-mono">
        {prettyJson(json)}
      </pre>
    </DetailPanel>
  );
}

/** A link styled like the rest of the detail pages' links. */
export function DetailLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <Link className="text-accent-fg hover:underline" to={to}>
      {children}
    </Link>
  );
}
