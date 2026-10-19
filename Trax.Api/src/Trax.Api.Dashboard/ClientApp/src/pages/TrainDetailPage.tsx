import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useClient, useQuery } from "urql";
import {
  EXECUTIONS,
  MACHINES,
  MANIFESTS,
  MANIFEST_SCHEDULE,
  METRIC_SERIES,
  RUN_GRAPH,
  TRAIN_CHAINS,
  TRAIN_STATS,
} from "../graphql/queries";
import { Graph } from "../components/Graph";
import { ExecutionsOverTimeChart } from "../components/OverviewCharts";
import { QueueTrainDialog } from "../components/QueueTrainDialog";
import { RunTrainDialog } from "../components/RunTrainDialog";
import { StateBadge } from "../components/StateBadge";
import { TermHelp } from "../components/TermHelp";
import { useAnswers } from "../lib/answerable";
import { enumLabel, formatMs, formatTime, shortName } from "../lib/format";
import { chainGraph, failuresByStep, GRID_RUNS, STEP_STATE_LABEL, stepRows, type StepRow } from "../lib/trainChain";
import { useUrlState } from "../lib/urlState";
import type {
  ExecutionSummary,
  ManifestSummary,
  PagedResult,
  RunGraph,
  RunNodeState,
  TrainChain,
  TrainExecutionStats,
  TrainState,
} from "../types";

// A train's page: what it does (its declared chain, where its runs fail), how it is doing (a grid of
// its recent runs by step, its runs over time), what starts it (its manifests, the machine states
// that invoke it) and Run and Queue. Every part is built from reads the console already has.

// The recent-runs read the page has always made; the grid draws its newest GRID_RUNS.
const RECENT_TAKE = 25;
// The failed runs of a range read to place failures on steps; past it the counts say "at least".
const FAILED_TAKE = 500;
const DOCS_TRAINS = "https://traxsharp.net/docs/core/trains-and-junctions";
const DOCS_SCHEDULER = "https://traxsharp.net/docs/scheduler";

type Range = "24h" | "7d";
const RANGES: Record<Range, { label: string; series: string; ms: number }> = {
  "24h": { label: "last 24 hours", series: "LAST24_HOURS", ms: 24 * 3600_000 },
  "7d": { label: "last 7 days", series: "LAST7_DAYS", ms: 7 * 24 * 3600_000 },
};

interface SeriesBucket {
  start: string;
  completed: number;
  failed: number;
  cancelled: number;
  p50DurationMs: number | null;
  p95DurationMs: number | null;
}

interface MachineDefinition {
  name: string;
  systemOwned: boolean;
  states: { name: string; invokesTrain: string | null }[];
}

export function TrainDetailPage() {
  const { name = "" } = useParams();
  const trainName = decodeURIComponent(name);
  const short = shortName(trainName);
  const [rangeKey, setRange] = useUrlState<Range>("range", "24h");
  const range = RANGES[rangeKey] ?? RANGES["24h"];
  const [dialog, setDialog] = useState<"run" | "queue" | null>(null);

  // Reads a recording-backed demo may not answer yet are not sent there (lib/answerable.ts). The
  // range's reads go together: the series and the failures placed on steps.
  const answersChain = useAnswers("TrainChains");
  const answersRunGraph = useAnswers("RunGraph");
  const answersRange = useAnswers("MetricSeries");
  const answersSchedule = useAnswers("ManifestSchedule");
  const answersMachines = useAnswers("Machines");

  const [statsResult] = useQuery<{ operations: { trainStats: TrainExecutionStats } }>({
    query: TRAIN_STATS,
    variables: { trainName },
  });
  const stats = statsResult.data?.operations?.trainStats;

  const [execResult] = useQuery<{ operations: { executions: PagedResult<ExecutionSummary> } }>({
    query: EXECUTIONS,
    variables: { take: RECENT_TAKE, trainName, order: "NEWEST" },
  });
  const recent = execResult.data?.operations?.executions?.items;

  // Every train's chain comes in one read; the page picks its own. Admin trains are included, so
  // a scheduler train's page draws its chain too.
  const [chainResult] = useQuery<{ operations: { trains: TrainChain[] } }>({
    query: TRAIN_CHAINS,
    variables: { hideAdmin: false },
    pause: !answersChain,
  });
  const chainTrains = chainResult.data?.operations?.trains;
  const chain = chainTrains?.find((t) => t.fullName === trainName)?.chain;
  const steps = useMemo(() => (chain?.hasGraph ? chain.allNodes : []), [chain]);

  // The range runs back from when the page loaded (to the minute), so a re-render asks nothing again.
  const [loadedAt] = useState(() => Math.floor(Date.now() / 60_000) * 60_000);
  const startedAfter = new Date(loadedAt - range.ms).toISOString();
  const [failedResult] = useQuery<{ operations: { executions: PagedResult<ExecutionSummary> } }>({
    query: EXECUTIONS,
    variables: { take: FAILED_TAKE, trainName, trainState: "FAILED", startedAfter, order: "NEWEST" },
    pause: !answersRange,
  });
  const failedPage = failedResult.data?.operations?.executions;
  const failures = useMemo(() => failuresByStep(steps, failedPage?.items ?? []), [steps, failedPage]);
  const failuresTitle = `Failed runs in the ${range.label}`;
  const graph = useMemo(
    () => chainGraph(steps, failures.byStep, failuresTitle),
    [steps, failures, failuresTitle],
  );

  const [seriesResult] = useQuery<{
    operations: { metrics: { series: { bucketSeconds: number; buckets: SeriesBucket[] } } };
  }>({
    query: METRIC_SERIES,
    variables: { scope: "TRAIN", range: range.series, name: trainName },
    pause: !answersRange,
  });
  const series = seriesResult.data?.operations?.metrics?.series;

  const [manifestsResult] = useQuery<{ operations: { manifests: PagedResult<ManifestSummary> } }>({
    query: MANIFESTS,
    variables: { take: 25, nameContains: trainName, hideAdminTrains: false },
    pause: !answersSchedule,
  });
  // nameContains matches part of a name; the train's manifests are the ones that name it exactly.
  const manifests = manifestsResult.data?.operations?.manifests?.items.filter((m) => m.name === trainName);

  const [machinesResult] = useQuery<{ operations: { machines: MachineDefinition[] } }>({
    query: MACHINES,
    pause: !answersMachines,
  });
  const invokers = (machinesResult.data?.operations?.machines ?? []).flatMap((m) =>
    m.states.filter((s) => s.invokesTrain === trainName).map((s) => ({ machine: m.name, state: s.name })),
  );

  const gridRuns = useMemo(() => (recent ?? []).slice(0, GRID_RUNS).reverse(), [recent]);
  const runGraphs = useRunGraphs(
    gridRuns.map((r) => r.id),
    !answersRunGraph || steps.length === 0,
  );

  const neverRan = recent != null && recent.length === 0 && (stats == null || stats.total === 0);
  const successRate = stats && stats.total > 0 ? Math.round((stats.completed / stats.total) * 100) : null;
  const error = statsResult.error ?? execResult.error ?? chainResult.error;

  return (
    <div>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <Link to="/trains" className="text-sm text-accent-fg hover:underline">
            ← Trains
          </Link>
          <div className="mt-1 flex items-center gap-2">
            <h1 className="text-2xl font-bold text-fg">{short}</h1>
            <TermHelp term="train" />
          </div>
          <p className="text-xs text-muted font-mono break-all">{trainName}</p>
        </div>
        <div className="flex items-center gap-2">
          {answersRange && (
            <label className="flex items-center gap-2 text-sm text-fg-2">
              Range
              <select
                aria-label="Range"
                value={rangeKey}
                onChange={(e) => setRange(e.target.value as Range)}
                className="text-sm border border-line-strong rounded-md px-2 py-1 bg-field"
              >
                <option value="24h">Last 24 hours</option>
                <option value="7d">Last 7 days</option>
              </select>
            </label>
          )}
          <button
            onClick={() => setDialog("queue")}
            className="text-sm px-3 py-1.5 rounded-md bg-accent text-on-accent hover:bg-accent-hover"
          >
            Queue
          </button>
          <button
            onClick={() => setDialog("run")}
            className="text-sm px-3 py-1.5 rounded-md border border-line-strong text-fg-2 hover:bg-hover"
          >
            Run
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {error.message}
        </div>
      )}

      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3 mb-6">
        <Kpi label="Total runs" value={stats?.total.toLocaleString() ?? "—"} />
        <Kpi
          label="Success rate"
          value={successRate == null ? "—" : `${successRate}%`}
          tone={successRate != null && successRate < 90 ? "warn" : "default"}
        />
        <Kpi label="Failed" value={stats?.failed.toLocaleString() ?? "—"} tone={stats && stats.failed > 0 ? "warn" : "default"} />
        <Kpi label="Running" value={stats?.inProgress.toLocaleString() ?? "—"} />
        <Kpi label="Avg duration" value={stats?.averageMilliseconds != null ? formatMs(stats.averageMilliseconds) : "—"} />
        <Kpi label="Last success" value={formatTime(stats?.lastSuccessfulRun ?? null)} small />
      </div>

      {answersChain && (
        <section aria-label="Chain" className="mb-6">
          <h2 className="text-sm font-semibold text-fg mb-2">Chain</h2>
          {chainTrains && !chain ? (
            <p className="text-sm text-muted">This host does not register this train, so its chain is not known here.</p>
          ) : chain && !chain.hasGraph ? (
            <p className="text-sm text-muted">
              This host cannot read the train's chain, so its steps are not drawn.{" "}
              <a href={DOCS_TRAINS} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
                How a train's chain is declared
              </a>
            </p>
          ) : (
            <>
              <Graph
                nodes={graph.nodes}
                edges={graph.edges}
                ariaLabel={`Chain of ${short}`}
                emptyText={chainResult.fetching ? "Reading the chain…" : "This train declares no steps."}
              />
              {answersRange && failedPage && (
                <p className="mt-2 text-xs text-muted">
                  {failedPage.items.length === 0
                    ? `No failed runs in the ${range.label}.`
                    : `${failedPage.nextCursor != null ? "At least " : ""}${failedPage.items.length.toLocaleString()} failed ${
                        failedPage.items.length === 1 ? "run" : "runs"
                      } in the ${range.label}; the red count on a step is how many stopped there.`}
                  {failures.elsewhere > 0 &&
                    ` ${failures.elsewhere} stopped outside a step drawn here, or at a junction more than one step runs.`}
                </p>
              )}
            </>
          )}
        </section>
      )}

      <section aria-label="Recent runs" className="mb-6">
        <div className="flex items-baseline justify-between mb-2">
          <div className="flex items-center gap-2">
            <h2 className="text-sm font-semibold text-fg">Recent runs</h2>
            <TermHelp term="run" />
          </div>
          {recent && recent.length > 0 && (
            <Link to={`/runs?train=${encodeURIComponent(trainName)}`} className="text-xs text-accent-fg hover:underline">
              All runs of this train
            </Link>
          )}
        </div>
        {neverRan ? (
          <div className="bg-surface rounded-lg border border-line p-6 text-center">
            <p className="text-sm text-fg-2">This train has not run yet.</p>
            <p className="text-sm text-muted mt-1">
              Run it now, queue it for a worker, or give it a manifest so it runs on a schedule.{" "}
              <a href={DOCS_SCHEDULER} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
                Scheduling a train
              </a>
            </p>
            <div className="mt-4 flex justify-center gap-2">
              <button
                onClick={() => setDialog("run")}
                className="text-sm px-3 py-1.5 rounded-md bg-accent text-on-accent hover:bg-accent-hover"
              >
                Run now
              </button>
              <button
                onClick={() => setDialog("queue")}
                className="text-sm px-3 py-1.5 rounded-md border border-line-strong text-fg-2 hover:bg-hover"
              >
                Queue a run
              </button>
            </div>
          </div>
        ) : (
          <RunGrid runs={gridRuns} rows={stepRows(steps)} graphs={runGraphs} drawSteps={answersRunGraph} />
        )}
      </section>

      <div className="grid gap-6 md:grid-cols-2 mb-6">
        {answersSchedule && (
          <section aria-label="Manifests">
            <div className="flex items-center gap-2 mb-2">
              <h2 className="text-sm font-semibold text-fg">Manifests</h2>
              <TermHelp term="manifest" />
            </div>
            <div className="bg-surface rounded-lg border border-line overflow-hidden">
              {manifests && manifests.length === 0 ? (
                <p className="px-4 py-6 text-sm text-muted text-center">
                  No manifest runs this train; it runs only when someone runs or queues it, or another train or a
                  machine starts it.{" "}
                  <a href={DOCS_SCHEDULER} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
                    Scheduling a train
                  </a>
                </p>
              ) : (
                <ul className="divide-y divide-line">
                  {(manifests ?? []).slice(0, 10).map((m) => (
                    <ManifestRow key={m.id} manifest={m} />
                  ))}
                </ul>
              )}
            </div>
          </section>
        )}
        {answersMachines && (
          <section aria-label="Started by machines">
            <div className="flex items-center gap-2 mb-2">
              <h2 className="text-sm font-semibold text-fg">Started by machines</h2>
              <TermHelp term="machine" />
            </div>
            <div className="bg-surface rounded-lg border border-line overflow-hidden">
              {machinesResult.data && invokers.length === 0 ? (
                <p className="px-4 py-6 text-sm text-muted text-center">No state machine's state starts this train.</p>
              ) : (
                <ul className="divide-y divide-line">
                  {invokers.map((i) => (
                    <li key={`${i.machine}/${i.state}`} className="px-4 py-2 text-sm flex items-center justify-between gap-2">
                      <span>
                        <Link
                          to={`/machines?machine=${encodeURIComponent(i.machine)}`}
                          className="font-medium text-accent-fg hover:underline"
                        >
                          {i.machine}
                        </Link>
                        <span className="text-muted"> in </span>
                        <Link
                          to={`/machines?machine=${encodeURIComponent(i.machine)}&state=${encodeURIComponent(i.state)}`}
                          className="text-accent-fg hover:underline"
                          aria-label={`Instances of ${i.machine} in ${i.state}`}
                        >
                          {i.state}
                        </Link>
                      </span>
                      <span className="text-xs text-muted">starts a run on entering the state</span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </section>
        )}
      </div>

      {answersRange && (
        <section aria-label="Runs over time" className="mb-6">
          <h2 className="text-sm font-semibold text-fg mb-2">Runs over time, {range.label}</h2>
          <div className="bg-surface rounded-lg border border-line p-4">
            {seriesResult.error ? (
              <p className="text-sm text-danger-fg">{seriesResult.error.message}</p>
            ) : series ? (
              <>
                <ExecutionsOverTimeChart
                  buckets={series.buckets.map((b) => ({
                    timestamp: b.start,
                    completed: b.completed,
                    failed: b.failed,
                    cancelled: b.cancelled,
                  }))}
                />
                <SeriesTotals buckets={series.buckets} />
              </>
            ) : (
              <p className="text-sm text-muted">Reading the series…</p>
            )}
          </div>
        </section>
      )}

      {dialog === "queue" && (
        <QueueTrainDialog initialTrain={trainName} onQueued={() => {}} onClose={() => setDialog(null)} />
      )}
      {dialog === "run" && <RunTrainDialog initialTrain={trainName} onClose={() => setDialog(null)} />}
    </div>
  );
}

// Each run's graph, read once per run (the client's cache answers a run read before); null until
// its read answers, and left out when it fails.
function useRunGraphs(ids: number[], pause: boolean): ReadonlyMap<number, RunGraph> {
  const client = useClient();
  const [graphs, setGraphs] = useState<ReadonlyMap<number, RunGraph>>(new Map());
  const key = ids.join(",");
  useEffect(() => {
    if (pause || key === "") return;
    let live = true;
    const wanted = key.split(",").map(Number);
    Promise.all(
      wanted.map((metadataId) =>
        client
          .query<{ operations: { runGraph: RunGraph | null } }>(RUN_GRAPH, { metadataId })
          .toPromise()
          .then((r) => [metadataId, r.data?.operations?.runGraph ?? null] as const),
      ),
    ).then((answers) => {
      if (!live) return;
      const next = new Map<number, RunGraph>();
      for (const [id, g] of answers) if (g) next.set(id, g);
      setGraphs(next);
    });
    return () => {
      live = false;
    };
  }, [client, key, pause]);
  return graphs;
}

const RUN_TONE: Record<TrainState, string> = {
  PENDING: "bg-raised border-line-strong",
  IN_PROGRESS: "bg-info border-info",
  COMPLETED: "bg-ok border-ok",
  FAILED: "bg-danger border-danger",
  CANCELLED: "bg-warn border-warn",
};

const STEP_TONE: Record<RunNodeState, string> = {
  NOT_REACHED: "bg-transparent border-line",
  IN_PROGRESS: "bg-info border-info",
  COMPLETED: "bg-ok border-ok",
  FAILED: "bg-danger border-danger",
  CANCELLED: "bg-warn border-warn",
  SKIPPED: "bg-sunken border-line",
  NOT_RECORDED: "bg-transparent border-dashed border-line-strong",
  WITHHELD: "bg-sunken border-dashed border-line-strong",
  RESTORED: "bg-accent border-accent",
  INTERRUPTED: "bg-transparent border-dashed border-warn",
};

const GRID_LEGEND: RunNodeState[] = ["COMPLETED", "FAILED", "IN_PROGRESS", "CANCELLED", "RESTORED", "SKIPPED", "NOT_REACHED"];

// Airflow's grid: a column per run, oldest on the left, and a row per step, each cell that step's
// state in that run. The top row is the run itself. Every cell opens its run.
function RunGrid({
  runs,
  rows,
  graphs,
  drawSteps,
}: {
  runs: ExecutionSummary[];
  rows: StepRow[];
  graphs: ReadonlyMap<number, RunGraph>;
  drawSteps: boolean;
}) {
  const stateOf = useMemo(() => {
    const byRun = new Map<number, Map<string, RunNodeState>>();
    for (const [id, g] of graphs) byRun.set(id, new Map(g.allNodes.map((n) => [n.id, n.state])));
    return byRun;
  }, [graphs]);

  if (runs.length === 0) return <p className="text-sm text-muted py-4">Reading the runs…</p>;
  const cell = "block h-4 w-4 rounded-sm border";
  return (
    <div className="bg-surface rounded-lg border border-line p-3">
      <div className="overflow-x-auto">
        <table aria-label="Recent runs by step" className="text-xs border-separate border-spacing-1">
          <thead>
            <tr>
              <th scope="col" className="text-left font-medium text-muted pr-3 min-w-40">
                Run
              </th>
              {runs.map((r) => (
                <th key={r.id} scope="col" className="p-0">
                  <Link
                    to={`/runs/${r.id}`}
                    aria-label={`Run #${r.id}: ${enumLabel(r.trainState)}`}
                    title={`#${r.id} · ${enumLabel(r.trainState)} · ${formatTime(r.startTime)}`}
                    className={`${cell} ${RUN_TONE[r.trainState]} hover:ring-2 hover:ring-accent`}
                  />
                </th>
              ))}
            </tr>
          </thead>
          {drawSteps && rows.length > 0 && (
            <tbody>
              {rows.map((step) => (
                <tr key={step.id}>
                  <th
                    scope="row"
                    className="text-left font-normal text-fg-2 pr-3 whitespace-nowrap"
                    style={{ paddingLeft: `${step.depth * 12}px` }}
                    title={step.track ? `${step.id} (track ${step.track})` : step.id}
                  >
                    {step.label}
                  </th>
                  {runs.map((r) => {
                    const state = stateOf.get(r.id)?.get(step.id);
                    const label = state ? STEP_STATE_LABEL[state] : "Not read";
                    return (
                      <td key={r.id} className="p-0">
                        <Link
                          to={`/runs/${r.id}`}
                          aria-label={`Run #${r.id}, ${step.label}: ${label}`}
                          title={`#${r.id} · ${step.label}: ${label}`}
                          className={`${cell} ${state ? STEP_TONE[state] : "border-dashed border-line"} hover:ring-2 hover:ring-accent`}
                        />
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          )}
        </table>
      </div>
      <ul aria-label="Legend" className="mt-2 flex flex-wrap gap-3 text-xs text-muted">
        {GRID_LEGEND.map((s) => (
          <li key={s} className="flex items-center gap-1.5">
            <span aria-hidden className={`h-3 w-3 rounded-sm border ${STEP_TONE[s]}`} />
            {STEP_STATE_LABEL[s]}
          </li>
        ))}
      </ul>
      <p className="mt-1 text-xs text-muted">
        The newest {runs.length} {runs.length === 1 ? "run" : "runs"}, oldest on the left; the newest is <StateBadge state={runs[runs.length - 1].trainState} />.
      </p>
    </div>
  );
}

function ManifestRow({ manifest }: { manifest: ManifestSummary }) {
  const [{ data }] = useQuery<{
    operations: { manifestDetail: { id: number; scheduleText: string; nextRuns: string[] } | null };
  }>({ query: MANIFEST_SCHEDULE, variables: { id: manifest.id, count: 1 } });
  const schedule = data?.operations?.manifestDetail;
  const next = schedule?.nextRuns[0];
  return (
    <li className="px-4 py-2 text-sm flex items-center justify-between gap-3">
      <span className="min-w-0">
        <Link to={`/schedules/${manifest.id}`} className="font-medium text-accent-fg hover:underline">
          {manifest.externalId}
        </Link>
        <span className="block text-xs text-muted">{schedule?.scheduleText ?? enumLabel(manifest.scheduleType)}</span>
      </span>
      <span className="text-xs text-right text-fg-2 whitespace-nowrap">
        {!manifest.isEnabled ? (
          <span className="text-warn-fg">Disabled</span>
        ) : next ? (
          <>
            <span className="text-muted">Next run </span>
            {formatTime(next)}
          </>
        ) : schedule ? (
          <span className="text-muted">No run due</span>
        ) : null}
      </span>
    </li>
  );
}

function SeriesTotals({ buckets }: { buckets: SeriesBucket[] }) {
  const sum = (k: "completed" | "failed" | "cancelled") => buckets.reduce((n, b) => n + b[k], 0);
  const p95 = buckets.map((b) => b.p95DurationMs).filter((v): v is number => v != null);
  return (
    <p className="mt-2 text-xs text-muted">
      {sum("completed").toLocaleString()} completed, {sum("failed").toLocaleString()} failed,{" "}
      {sum("cancelled").toLocaleString()} cancelled
      {p95.length > 0 && `; the slowest bucket's 95th percentile was ${formatMs(Math.max(...p95))}`}.
    </p>
  );
}

function Kpi({
  label,
  value,
  tone = "default",
  small = false,
}: {
  label: string;
  value: string;
  tone?: "default" | "warn";
  small?: boolean;
}) {
  return (
    <div className="bg-surface rounded-lg border border-line p-3">
      <p className="text-xs text-muted">{label}</p>
      <p className={`${small ? "text-sm mt-1" : "text-xl"} font-bold ${tone === "warn" ? "text-danger-fg" : "text-fg"}`}>
        {value}
      </p>
    </div>
  );
}
