import { Link, useParams } from "react-router-dom";
import { useQuery } from "urql";
import { EXECUTIONS, TRAIN_STATS } from "../graphql/queries";
import { StateBadge } from "../components/StateBadge";
import type {
  ExecutionSummary,
  PagedResult,
  TrainExecutionStats,
} from "../types";

const PAGE_SIZE = 25;

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}

function fmtDuration(ms: number | null): string {
  if (ms == null) return "—";
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}

function fmtDate(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "—";
}

export function TrainDetailPage() {
  const { name = "" } = useParams();
  const trainName = decodeURIComponent(name);

  const [statsResult] = useQuery<{ operations: { trainStats: TrainExecutionStats } }>({
    query: TRAIN_STATS,
    variables: { trainName },
  });
  const stats = statsResult.data?.operations?.trainStats;

  const [execResult] = useQuery<{
    operations: { executions: PagedResult<ExecutionSummary> };
  }>({
    query: EXECUTIONS,
    variables: { take: PAGE_SIZE, trainName, order: "NEWEST" },
  });
  const recent = execResult.data?.operations?.executions?.items ?? [];

  const successRate =
    stats && stats.total > 0 ? Math.round((stats.completed / stats.total) * 100) : null;

  return (
    <div>
      <div className="mb-6">
        <Link to="/trains" className="text-sm text-accent-fg hover:underline">
          ← Trains
        </Link>
        <h1 className="text-2xl font-bold text-fg mt-1">
          {shortName(trainName)}
        </h1>
        <p className="text-xs text-muted font-mono break-all">{trainName}</p>
      </div>

      {(statsResult.error || execResult.error) && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {statsResult.error?.message ?? execResult.error?.message}
        </div>
      )}

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        <Kpi label="Total runs" value={stats?.total.toLocaleString() ?? "—"} />
        <Kpi
          label="Success rate"
          value={successRate == null ? "—" : `${successRate}%`}
          tone={successRate != null && successRate < 90 ? "warn" : "default"}
        />
        <Kpi label="Failed" value={stats?.failed.toLocaleString() ?? "—"} tone={stats && stats.failed > 0 ? "warn" : "default"} />
        <Kpi label="Running" value={stats?.inProgress.toLocaleString() ?? "—"} />
        <Kpi label="Avg duration" value={fmtDuration(stats?.averageMilliseconds ?? null)} />
        <Kpi label="Completed" value={stats?.completed.toLocaleString() ?? "—"} />
        <Kpi label="Last run" value={fmtDate(stats?.lastRun ?? null)} small />
        <Kpi label="Last success" value={fmtDate(stats?.lastSuccessfulRun ?? null)} small />
      </div>

      <h2 className="text-sm font-semibold text-fg mb-3">
        Recent executions
      </h2>
      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium">State</th>
              <th className="px-4 py-2 font-medium">Started</th>
              <th className="px-4 py-2 font-medium">External ID</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {recent.map((e) => (
              <tr key={e.id}>
                <td className="px-4 py-2">
                  <Link to={`/executions/${e.id}`} className="hover:underline">
                    <StateBadge state={e.trainState} />
                  </Link>
                </td>
                <td className="px-4 py-2 text-fg-2">
                  {new Date(e.startTime).toLocaleString()}
                </td>
                <td className="px-4 py-2 text-muted font-mono text-xs">
                  {e.externalId}
                </td>
              </tr>
            ))}
            {recent.length === 0 && (
              <tr>
                <td colSpan={3} className="px-4 py-8 text-center text-muted">
                  No executions for this train.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
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
    <div className="bg-surface rounded-lg border border-line p-4">
      <p className="text-xs text-muted">{label}</p>
      <p
        className={`${small ? "text-sm" : "text-2xl"} font-bold ${
          tone === "warn"
            ? "text-danger-fg"
            : "text-fg"
        }`}
      >
        {value}
      </p>
    </div>
  );
}
