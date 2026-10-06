import { useEffect, useState } from "react";
import { useQuery, useSubscription } from "urql";
import { OVERVIEW } from "../graphql/queries";
import { ON_TRAIN_STATE_CHANGED } from "../graphql/subscriptions";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { useRefetchOnReconnect } from "../lib/useRefetchOnReconnect";
import { useHideAdminTrains } from "../lib/adminTrains";
import { useOverviewPanels } from "../lib/overviewPanels";
import {
  ExecutionsOverTimeChart,
  ThroughputChart,
  TopBarChart,
} from "../components/OverviewCharts";
import type {
  DashboardMetrics,
  HealthStatus,
  MetricsRange,
  ServerMetrics,
  TrainLifecycleEvent,
} from "../types";

const POLL_MS = 5000;

const RANGES: { value: MetricsRange; label: string; chartLabel: string }[] = [
  { value: "LAST24_HOURS", label: "24h", chartLabel: "last 24h" },
  { value: "LAST60_MINUTES", label: "1h", chartLabel: "last hour" },
];

interface OverviewData {
  operations: {
    health: HealthStatus;
    metrics: {
      server: ServerMetrics;
      serverCpuPercent: number | null;
      dashboard: DashboardMetrics;
    };
  };
}

export function OverviewPage() {
  const [range, setRange] = useState<MetricsRange>("LAST24_HOURS");
  const hideAdmin = useHideAdminTrains();
  // Which panels the user shows (User settings > Overview panels).
  const show = useOverviewPanels();
  const [result, reexecute] = useQuery<OverviewData>({
    query: OVERVIEW,
    variables: { range, hideAdmin },
  });

  // metrics.dashboard is the heaviest read, so refresh on an interval rather than
  // on every interaction (see the scaling notes in the API reference).
  useEffect(() => {
    const id = setInterval(
      () => reexecute({ requestPolicy: "network-only" }),
      POLL_MS,
    );
    return () => clearInterval(id);
  }, [reexecute]);

  // Refresh once when the live socket recovers, so a drop doesn't leave stale KPIs until the poll.
  useRefetchOnReconnect(() => reexecute({ requestPolicy: "network-only" }));

  // Live nudge: when executions change, refresh sooner than the poll. The handler returns
  // the latest event, and the debounced effect coalesces bursts into a single refetch so a
  // flood of events never hammers the heavy metrics query.
  const [{ data: lastEvent }] = useSubscription<
    { onTrainStateChanged: TrainLifecycleEvent },
    TrainLifecycleEvent
  >({ query: ON_TRAIN_STATE_CHANGED }, (_prev, data) => data.onTrainStateChanged);

  useEffect(() => {
    if (!lastEvent) return;
    const t = setTimeout(
      () => reexecute({ requestPolicy: "network-only" }),
      1500,
    );
    return () => clearTimeout(t);
  }, [lastEvent, reexecute]);

  // The KPI cards also count queue depth, unresolved dead letters, and reflect config changes, so
  // nudge on those domains too (executions are already covered by the lifecycle subscription above).
  useRefetchOnChange(["WORK_QUEUE", "DEAD_LETTER", "SCHEDULER_CONFIG"], () =>
    reexecute({ requestPolicy: "network-only" }),
  );

  if (result.error)
    return <ErrorBox message={result.error.message} />;
  if (!result.data)
    return <p className="text-muted">Loading…</p>;

  const { health, metrics } = result.data.operations;
  const { kpis } = metrics.dashboard;
  const rangeMeta = RANGES.find((r) => r.value === range)!;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-fg">Overview</h1>
        <div className="flex items-center gap-3">
          <div className="inline-flex rounded-lg border border-line-strong overflow-hidden text-sm">
            {RANGES.map((r) => (
              <button
                key={r.value}
                onClick={() => setRange(r.value)}
                aria-pressed={r.value === range}
                className={`px-3 py-1 ${
                  r.value === range
                    ? "bg-accent text-on-accent"
                    : "bg-surface text-fg-2 hover:bg-hover"
                }`}
              >
                {r.label}
              </button>
            ))}
          </div>
          <HealthPill status={health.status} />
        </div>
      </div>

      {show.summaryCards && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
          <Stat label="Executions today" value={kpis.executionsToday} />
          <Stat label="Success rate" value={`${kpis.successRate}%`} />
          <Stat label="Running now" value={kpis.currentlyRunning} />
          <Stat
            label="Unresolved dead letters"
            value={kpis.unresolvedDeadLetters}
            warn={kpis.unresolvedDeadLetters > 0}
          />
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
        <Panel title="Health">
          <dl className="grid grid-cols-2 gap-y-2 text-sm">
            <Row label="Queue depth" value={health.queueDepth} />
            <Row label="In progress" value={health.inProgress} />
            <Row label="Failed (last hour)" value={health.failedLastHour} />
            <Row label="Dead letters" value={health.deadLetters} />
          </dl>
          <p className="text-xs text-muted mt-3">{health.description}</p>
        </Panel>

        {show.serverHealth && (
          <Panel title="Server">
            <dl className="grid grid-cols-2 gap-y-2 text-sm">
              <Row label="Uptime" value={formatDuration(metrics.server.uptimeSeconds)} />
              <Row label="Working set" value={formatBytes(metrics.server.workingSetBytes)} />
              <Row label="GC heap" value={formatBytes(metrics.server.gcHeapBytes)} />
              <Row
                label="CPU"
                value={
                  metrics.serverCpuPercent == null
                    ? "—"
                    : `${metrics.serverCpuPercent.toFixed(1)}%`
                }
              />
            </dl>
          </Panel>
        )}
      </div>

      {show.executionsChart && (
        <>
          <Panel title={`Executions over time (${rangeMeta.chartLabel})`}>
            <ExecutionsOverTimeChart buckets={metrics.dashboard.executionsOverTime} />
          </Panel>

          <div className="mt-8">
            <Panel title="Throughput by train (7d)">
              <ThroughputChart series={metrics.dashboard.throughputSeries} />
            </Panel>
          </div>
        </>
      )}

      {(show.failures || show.avgDuration) && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mt-8">
          {show.failures && (
            <Panel title="Top failures (7d)">
              <TopBarChart
                rows={metrics.dashboard.topFailures.map((f) => ({
                  name: f.trainName,
                  value: f.count,
                }))}
                color="var(--chart-failed)"
                empty="No failures"
              />
            </Panel>
          )}
          {show.avgDuration && (
            <Panel title="Slowest trains (7d)">
              <TopBarChart
                rows={metrics.dashboard.topAverageDurations.map((d) => ({
                  name: d.trainName,
                  value: Math.round(d.averageMilliseconds),
                }))}
                color="var(--chart-series-1)"
                unit="ms"
                empty="No completed runs"
              />
            </Panel>
          )}
        </div>
      )}
    </div>
  );
}

function Stat({
  label,
  value,
  warn,
}: {
  label: string;
  value: string | number;
  warn?: boolean;
}) {
  return (
    <div className="bg-surface rounded-lg border border-line p-5">
      <p className="text-sm text-muted">{label}</p>
      <p
        className={`text-3xl font-bold mt-1 ${
          warn ? "text-danger-fg" : "text-fg"
        }`}
      >
        {value}
      </p>
    </div>
  );
}

function Panel({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className="bg-surface rounded-lg border border-line p-5">
      <h2 className="text-sm font-semibold text-fg mb-3">{title}</h2>
      {children}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string | number }) {
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className="text-fg font-medium text-right">{value}</dd>
    </>
  );
}

function HealthPill({ status }: { status: string }) {
  const healthy = status.toLowerCase() === "healthy";
  return (
    <span
      className={`text-sm font-medium px-3 py-1 rounded-full ${
        healthy ? "bg-ok-soft text-ok-fg" : "bg-warn-soft text-warn-fg"
      }`}
    >
      {status}
    </span>
  );
}

function ErrorBox({ message }: { message: string }) {
  return (
    <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm">
      {message}
    </div>
  );
}

function formatBytes(bytes: number): string {
  const mb = bytes / (1024 * 1024);
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
}

function formatDuration(seconds: number): string {
  const s = Math.floor(seconds);
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}
