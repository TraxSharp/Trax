import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useQuery, useSubscription } from "urql";
import { ATTENTION, OVERVIEW } from "../graphql/queries";
import { ON_TRAIN_STATE_CHANGED } from "../graphql/subscriptions";
import { useAnswers } from "../lib/answerable";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { useRefetchOnReconnect } from "../lib/useRefetchOnReconnect";
import { useHideAdminTrains } from "../lib/adminTrains";
import { useOverviewPanels } from "../lib/overviewPanels";
import { shortName } from "../lib/format";
import { TermHelp } from "../components/TermHelp";
import {
  ExecutionsOverTimeChart,
  ThroughputChart,
  TopBarChart,
} from "../components/OverviewCharts";
import type {
  Attention,
  DashboardMetrics,
  HealthStatus,
  MetricsRange,
  TrainLifecycleEvent,
} from "../types";

const POLL_MS = 5000;

const GETTING_STARTED = "https://traxsharp.net/docs/getting-started";

const RANGES: { value: MetricsRange; label: string; chartLabel: string; bucketMinutes: number }[] = [
  { value: "LAST24_HOURS", label: "24h", chartLabel: "last 24h", bucketMinutes: 60 },
  { value: "LAST60_MINUTES", label: "1h", chartLabel: "last hour", bucketMinutes: 1 },
];

interface OverviewData {
  operations: {
    health: HealthStatus;
    metrics: { dashboard: DashboardMetrics };
  };
}

interface AttentionData {
  operations: { attention: Attention };
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
  // The demo answers only what its recordings hold; without the attention read the page still
  // draws its charts.
  const answersAttention = useAnswers("Attention");
  const [attentionResult, reexecuteAttention] = useQuery<AttentionData>({
    query: ATTENTION,
    pause: !answersAttention,
  });

  const refresh = () => {
    reexecute({ requestPolicy: "network-only" });
    if (answersAttention) reexecuteAttention({ requestPolicy: "network-only" });
  };

  // metrics.dashboard is the heaviest read, so refresh on an interval rather than
  // on every interaction (see the scaling notes in the API reference).
  useEffect(() => {
    const id = setInterval(() => {
      reexecute({ requestPolicy: "network-only" });
      if (answersAttention) reexecuteAttention({ requestPolicy: "network-only" });
    }, POLL_MS);
    return () => clearInterval(id);
  }, [reexecute, reexecuteAttention, answersAttention]);

  // Refresh once when the live socket recovers, so a drop doesn't leave stale KPIs until the poll.
  useRefetchOnReconnect(refresh);

  // Live nudge: when runs change, refresh sooner than the poll. The handler returns
  // the latest event, and the debounced effect coalesces bursts into a single refetch so a
  // flood of events never hammers the heavy metrics query.
  const [{ data: lastEvent }] = useSubscription<
    { onTrainStateChanged: TrainLifecycleEvent },
    TrainLifecycleEvent
  >({ query: ON_TRAIN_STATE_CHANGED }, (_prev, data) => data.onTrainStateChanged);

  useEffect(() => {
    if (!lastEvent) return;
    const t = setTimeout(() => {
      reexecute({ requestPolicy: "network-only" });
      if (answersAttention) reexecuteAttention({ requestPolicy: "network-only" });
    }, 1500);
    return () => clearTimeout(t);
  }, [lastEvent, reexecute, reexecuteAttention, answersAttention]);

  // The KPI cards and the attention list also count queue depth and dead letters and show the
  // scheduler switches, so nudge on those domains too (runs are covered above; stuck instances and
  // hosts wait for the poll).
  useRefetchOnChange(["WORK_QUEUE", "DEAD_LETTER", "SCHEDULER_CONFIG"], refresh);

  if (result.error)
    return <ErrorBox message={result.error.message} />;
  if (!result.data)
    return <p className="text-muted">Loading…</p>;

  const { health, metrics } = result.data.operations;
  const { kpis } = metrics.dashboard;
  const rangeMeta = RANGES.find((r) => r.value === range)!;
  const attention = attentionResult.data?.operations?.attention;
  const nothingRan = noRuns(metrics.dashboard, attention);

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3 mb-6">
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

      {answersAttention && (
        <AttentionPanel attention={attention} error={attentionResult.error?.message} />
      )}

      {nothingRan && <FirstRun />}

      {show.summaryCards && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
          <Stat label="Runs today" value={kpis.executionsToday} to="/runs" />
          <Stat label="Success rate" value={`${kpis.successRate}%`} />
          <Stat label="Running now" value={kpis.currentlyRunning} to="/runs?state=IN_PROGRESS" />
          <Stat
            label="Unresolved dead letters"
            value={kpis.unresolvedDeadLetters}
            warn={kpis.unresolvedDeadLetters > 0}
            to="/runs/dead-letters?status=AWAITING_INTERVENTION"
          />
        </div>
      )}

      <div className="mb-8">
        <Panel title="Health">
          <dl className="grid grid-cols-2 md:grid-cols-4 gap-y-2 gap-x-6 text-sm">
            <Row label="Queue depth" value={health.queueDepth} to="/runs/queued?status=QUEUED" />
            <Row label="In progress" value={health.inProgress} to="/runs?state=IN_PROGRESS" />
            <Row
              label="Failed (last hour)"
              value={health.failedLastHour}
              to="/runs?state=FAILED"
            />
            <Row label="Dead letters" value={health.deadLetters} to="/runs/dead-letters?status=AWAITING_INTERVENTION" />
          </dl>
          <p className="text-xs text-muted mt-3">{health.description}</p>
        </Panel>
      </div>

      {!nothingRan && show.executionsChart && (
        <>
          <Panel title={`Runs over time (${rangeMeta.chartLabel})`}>
            <ExecutionsOverTimeChart
              buckets={metrics.dashboard.executionsOverTime}
              linkFor={(timestamp, state) => {
                const start = new Date(timestamp);
                const end = new Date(start.getTime() + rangeMeta.bucketMinutes * 60_000);
                return `/runs?state=${state}&after=${localMinute(start)}&before=${localMinute(end)}`;
              }}
            />
          </Panel>

          <div className="mt-8">
            <Panel title="Throughput by train (7d)">
              <ThroughputChart
                series={metrics.dashboard.throughputSeries}
                linkFor={(trainName) => `/runs?state=COMPLETED&train=${encodeURIComponent(trainName)}`}
              />
            </Panel>
          </div>
        </>
      )}

      {!nothingRan && (show.failures || show.avgDuration) && (
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
                linkFor={(name) => `/runs?state=FAILED&train=${encodeURIComponent(name)}`}
                linkLabel={(name, value) => `${value} failed ${value === 1 ? "run" : "runs"} of ${shortName(name)}`}
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
                linkFor={(name) => `/trains/${encodeURIComponent(name)}`}
                linkLabel={(name, value) => `${shortName(name)} averages ${value} ms`}
              />
            </Panel>
          )}
        </div>
      )}
    </div>
  );
}

// Nothing in the window and nothing waiting: a new host, or one nobody has used this week.
function noRuns(dashboard: DashboardMetrics, attention: Attention | undefined): boolean {
  const { kpis } = dashboard;
  return (
    kpis.executionsToday === 0 &&
    kpis.currentlyRunning === 0 &&
    kpis.unresolvedDeadLetters === 0 &&
    dashboard.executionsOverTime.every((b) => b.completed + b.failed + b.cancelled === 0) &&
    dashboard.throughputSeries.length === 0 &&
    dashboard.topFailures.length === 0 &&
    dashboard.topAverageDurations.length === 0 &&
    (attention === undefined || (attention.queue.queued === 0 && attention.deadLetters.length === 0))
  );
}

/** What needs an operator now, each entry a link to the list it counts. */
function AttentionPanel({ attention, error }: { attention: Attention | undefined; error?: string }) {
  const items = attention ? attentionItems(attention) : [];
  return (
    <section aria-labelledby="attention-heading" className="bg-surface rounded-lg border border-line p-5 mb-8">
      <h2 id="attention-heading" className="text-sm font-semibold text-fg mb-3">
        Needs attention
      </h2>
      {error ? (
        <p className="text-sm text-danger-fg">{error}</p>
      ) : !attention ? (
        <p className="text-sm text-muted">Loading…</p>
      ) : items.length === 0 ? (
        <p className="text-sm text-ok-fg">
          Nothing needs attention: the scheduler is running, no dead letter is waiting, no train is failing more than
          usual, nothing is stuck and every host is beating.
        </p>
      ) : (
        <ul className="divide-y divide-line">
          {items.map((item) => (
            <li key={item.key}>
              <Link
                to={consolePath(item.link)}
                className="flex flex-wrap items-baseline gap-x-3 gap-y-1 py-2 text-sm hover:bg-hover rounded px-2 -mx-2"
              >
                <span
                  className={`font-medium ${item.tone === "danger" ? "text-danger-fg" : "text-warn-fg"}`}
                >
                  {item.title}
                </span>
                <span className="text-muted min-w-0 break-words">{item.detail}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

interface AttentionItem {
  key: string;
  title: string;
  detail: string;
  link: string;
  tone: "danger" | "warn";
}

function attentionItems(a: Attention): AttentionItem[] {
  const items: AttentionItem[] = [];
  if (!a.scheduler.jobDispatcherEnabled)
    items.push({
      key: "dispatcher",
      title: "Job dispatcher paused",
      detail: "Queued runs are not sent to workers until it is switched back on.",
      link: a.scheduler.link,
      tone: "danger",
    });
  if (!a.scheduler.manifestManagerEnabled)
    items.push({
      key: "manifest-manager",
      title: "Manifest manager paused",
      detail: "Manifests do not queue their scheduled runs until it is switched back on.",
      link: a.scheduler.link,
      tone: "danger",
    });
  for (const h of a.offlineHosts)
    items.push({
      key: `host-${h.instanceId}`,
      title: `Host ${h.hostName} offline`,
      detail: `Last heartbeat ${new Date(h.lastHeartbeatAt).toLocaleString()}${h.runsDispatcher ? ", it ran the job dispatcher" : ""}.`,
      link: h.link,
      tone: h.runsDispatcher ? "danger" : "warn",
    });
  for (const g of a.deadLetters)
    items.push({
      key: `dl-${g.trainName}-${g.reason}`,
      title: `${g.count} ${g.count === 1 ? "dead letter" : "dead letters"} of ${shortName(g.trainName)}`,
      detail: g.reason,
      link: g.link,
      tone: "danger",
    });
  for (const t of a.failingTrains)
    items.push({
      key: `failing-${t.trainName}`,
      title: `${shortName(t.trainName)} failing more than usual`,
      detail: `${t.failedLastHour} of ${t.finishedLastHour} runs failed in the last hour, against ${percent(t.baselineFailureRate)} over the week before.`,
      link: t.link,
      tone: "warn",
    });
  for (const s of a.stuckInstances)
    items.push({
      key: `stuck-${s.machine}-${s.state}`,
      title: `${s.count} stuck ${s.machine} ${s.count === 1 ? "instance" : "instances"} in ${s.state}`,
      detail: "A timer is overdue, or the run each waits on failed or was cancelled.",
      link: s.link,
      tone: "warn",
    });
  if (a.queue.queued > 0)
    items.push({
      key: "queue",
      title: `${a.queue.queued} queued ${a.queue.queued === 1 ? "run" : "runs"} waiting`,
      detail:
        a.queue.oldestAgeSeconds == null
          ? "Waiting for a worker."
          : `The oldest has waited ${formatAge(a.queue.oldestAgeSeconds)}.`,
      link: a.queue.link,
      tone: "warn",
    });
  return items;
}

/** The first-run state: nothing has run, so say what to do. */
function FirstRun() {
  return (
    <section aria-labelledby="first-run-heading" className="bg-surface rounded-lg border border-line p-5 mb-8">
      <h2 id="first-run-heading" className="text-sm font-semibold text-fg mb-2">
        No runs in the last 7 days
      </h2>
      <p className="text-sm text-fg-2">
        On a new host nothing has run yet. Queue a run of a train <TermHelp term="train" /> from the{" "}
        <Link to="/trains" className="text-accent-fg hover:underline">
          Trains page
        </Link>
        , or follow the{" "}
        <a href={GETTING_STARTED} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
          getting-started guide
        </a>{" "}
        to write and schedule your first train. Runs, failures and throughput show here as they happen.
      </p>
    </section>
  );
}

// A link the API sent is a console path; anything else opens the Overview rather than leaving it.
function consolePath(link: string): string {
  return link.startsWith("/") && !link.startsWith("//") ? link : "/";
}

function percent(rate: number): string {
  return `${Math.round(rate * 1000) / 10}%`;
}

function formatAge(seconds: number): string {
  const s = Math.floor(seconds);
  if (s < 60) return `${s}s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m`;
  const h = Math.floor(m / 60);
  if (h < 48) return `${h}h ${m % 60}m`;
  return `${Math.floor(h / 24)}d ${h % 24}h`;
}

// The Runs page's after/before filters are datetime-local values: local time to the minute.
function localMinute(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function Stat({
  label,
  value,
  warn,
  to,
}: {
  label: string;
  value: string | number;
  warn?: boolean;
  to?: string;
}) {
  const body = (
    <>
      <p className="text-sm text-muted">{label}</p>
      <p
        className={`text-3xl font-bold mt-1 ${
          warn ? "text-danger-fg" : "text-fg"
        }`}
      >
        {value}
      </p>
    </>
  );
  return to ? (
    <Link to={to} className="block bg-surface rounded-lg border border-line p-5 hover:bg-hover">
      {body}
    </Link>
  ) : (
    <div className="bg-surface rounded-lg border border-line p-5">{body}</div>
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

function Row({ label, value, to }: { label: string; value: string | number; to: string }) {
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className="text-fg font-medium text-right">
        <Link to={to} className="hover:underline" aria-label={`${label}: ${value}`}>
          {value}
        </Link>
      </dd>
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
