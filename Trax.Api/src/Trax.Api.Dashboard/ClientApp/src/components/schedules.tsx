import { useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useQuery } from "urql";
import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis } from "recharts";
import { METRIC_SERIES } from "../graphql/queries";
import { useAnswers } from "../lib/answerable";
import { untilText, utcText } from "../lib/schedules";
import type { MetricSeries, MetricSeriesRange } from "../types";

// The parts the Schedules area shares: the tab strip between manifests and groups, when a
// manifest runs next, and a run-history sparkline from operations.metrics.series.

const TABS = [
  { path: "/schedules", label: "Manifests" },
  { path: "/schedules/groups", label: "Manifest groups" },
];

/** The two views of Schedules, as tabs: each is its own route, so a link or a reload keeps it. */
export function SchedulesTabs() {
  const { pathname } = useLocation();
  const current = pathname.startsWith("/schedules/groups") ? "/schedules/groups" : "/schedules";
  return (
    <div role="tablist" aria-label="Schedules" className="mb-5 flex gap-1 border-b border-line">
      {TABS.map((t) => {
        const selected = t.path === current;
        return (
          <Link
            key={t.path}
            to={t.path}
            role="tab"
            aria-selected={selected}
            className={`-mb-px border-b-2 px-3 py-1.5 text-sm ${
              selected
                ? "border-accent font-medium text-accent-fg"
                : "border-transparent text-muted hover:text-fg"
            }`}
          >
            {t.label}
          </Link>
        );
      })}
    </div>
  );
}

/** The next run of a manifest, or what to say when it has none. */
export function NextRun({ runs, now }: { runs: string[] | undefined; now: number }) {
  if (runs === undefined) return <span className="text-muted">—</span>;
  if (runs.length === 0) return <span className="text-muted">Not scheduled</span>;
  return (
    <span title={runs.map(utcText).join("\n")}>
      <span className="text-fg-2">{untilText(runs[0], now)}</span>
      <span className="block text-xs text-muted">{utcText(runs[0])}</span>
    </span>
  );
}

const RANGES: { range: MetricSeriesRange; label: string }[] = [
  { range: "LAST24_HOURS", label: "24 hours" },
  { range: "LAST7_DAYS", label: "7 days" },
];

const TOOLTIP: React.CSSProperties = {
  background: "var(--surface)",
  border: "1px solid var(--line)",
  borderRadius: 8,
  fontSize: 12,
  color: "var(--fg)",
};

/**
 * Runs of one manifest or group over the last day or week, by outcome, with the totals in words
 * beside it. Not drawn where the client cannot answer the series (the demo, until it is recorded).
 */
export function RunSparkline({ scope, id }: { scope: "MANIFEST" | "GROUP"; id: number }) {
  const [range, setRange] = useState<MetricSeriesRange>("LAST24_HOURS");
  const answered = useAnswers("MetricSeries");
  const [{ data, error }] = useQuery<{ operations: { metrics: { series: MetricSeries } } }>({
    query: METRIC_SERIES,
    variables: { scope, range, id },
    pause: !answered,
  });
  if (!answered) return null;
  const series = data?.operations?.metrics?.series;
  const buckets = series?.buckets ?? [];
  const totals = buckets.reduce(
    (t, b) => ({ completed: t.completed + b.completed, failed: t.failed + b.failed, cancelled: t.cancelled + b.cancelled }),
    { completed: 0, failed: 0, cancelled: 0 },
  );
  const rows = buckets.map((b) => ({
    ...b,
    label: new Date(b.start).toLocaleString([], {
      month: range === "LAST7_DAYS" ? "short" : undefined,
      day: range === "LAST7_DAYS" ? "numeric" : undefined,
      hour: "2-digit",
      minute: "2-digit",
    }),
  }));
  const empty = series != null && totals.completed + totals.failed + totals.cancelled === 0;

  return (
    <section aria-label="Run history" className="mb-6 rounded-lg border border-line bg-surface p-4">
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-semibold text-fg">Run history</h2>
        <div className="flex gap-1" role="group" aria-label="Window">
          {RANGES.map((r) => (
            <button
              key={r.range}
              aria-pressed={range === r.range}
              onClick={() => setRange(r.range)}
              className={`rounded-md px-2 py-0.5 text-xs ${
                range === r.range ? "bg-accent-soft font-medium text-accent-fg" : "text-muted hover:bg-hover"
              }`}
            >
              {r.label}
            </button>
          ))}
        </div>
      </div>
      {error ? (
        <p className="text-sm text-danger-fg">{error.message}</p>
      ) : !series ? (
        <p className="text-sm text-muted">Loading…</p>
      ) : empty ? (
        <p className="text-sm text-muted">No runs in the last {range === "LAST7_DAYS" ? "7 days" : "24 hours"}.</p>
      ) : (
        <div className="flex flex-wrap items-end gap-4">
          <div className="h-12 min-w-0 flex-1" aria-hidden>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={rows} margin={{ top: 0, right: 0, left: 0, bottom: 0 }} barCategoryGap={1}>
                <XAxis dataKey="label" hide />
                <Tooltip contentStyle={TOOLTIP} cursor={{ fill: "var(--chart-cursor)" }} />
                <Bar dataKey="completed" stackId="a" fill="var(--chart-completed)" name="Completed" />
                <Bar dataKey="failed" stackId="a" fill="var(--chart-failed)" name="Failed" />
                <Bar dataKey="cancelled" stackId="a" fill="var(--chart-cancelled)" name="Cancelled" />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <p className="text-xs text-fg-2 whitespace-nowrap">
            <span className="text-ok-fg">{totals.completed} completed</span>
            {" · "}
            <span className={totals.failed > 0 ? "text-danger-fg" : undefined}>{totals.failed} failed</span>
            {" · "}
            {totals.cancelled} cancelled
          </p>
        </div>
      )}
    </section>
  );
}
