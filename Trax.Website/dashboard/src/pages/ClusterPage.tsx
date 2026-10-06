import { useQuery } from "urql";
import { HOSTS } from "../graphql/queries";
import { usePoll } from "../lib/poll";
import type { HostInfo } from "../types";
import { useNow } from "../lib/useNow";

// A host is "live" if it started work in the last minute, "idle" within 5 minutes, else "stale".
// Purely a display heuristic off last-seen; there is no heartbeat, so this is best-effort.
function freshness(lastSeen: string): { label: string; dot: string; text: string } {
  const ageMs = Date.now() - new Date(lastSeen).getTime();
  if (ageMs < 60_000)
    return { label: "Live", dot: "bg-ok", text: "text-ok-fg" };
  if (ageMs < 300_000)
    return { label: "Idle", dot: "bg-warn", text: "text-warn-fg" };
  return { label: "Stale", dot: "bg-idle", text: "text-muted" };
}

function ago(iso: string): string {
  const s = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (s < 60) return `${s}s ago`;
  const m = Math.round(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.round(h / 24)}d ago`;
}

interface HostsData {
  operations: { hosts: HostInfo[] };
}

export function ClusterPage() {
  const [result, reexecute] = useQuery<HostsData>({ query: HOSTS });
  // This is a full aggregation over the metadata table, so no aggressive auto-poll: refresh on
  // demand, with the user's fallback interval available if they want it.
  usePoll(() => reexecute({ requestPolicy: "network-only" }));

  const hosts = result.data?.operations?.hosts ?? [];
  const totalRunning = hosts.reduce((n, h) => n + h.currentlyRunning, 0);
  const now = useNow(5000);
  const liveCount = hosts.filter((h) => now - new Date(h.lastSeen).getTime() < 60_000).length;

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-4">
        <h1 className="text-2xl font-bold text-fg">Cluster</h1>
        <button
          onClick={() => reexecute({ requestPolicy: "network-only" })}
          className="text-sm px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 hover:bg-hover"
        >
          Refresh
        </button>
      </div>

      <p className="text-sm text-muted mb-4">
        Processes that have executed trains, rolled up by host instance from the execution history.
        Freshness is derived from each host's most recent run, not a heartbeat.
      </p>

      <div className="grid grid-cols-3 gap-4 mb-6">
        <Kpi label="Hosts" value={hosts.length} />
        <Kpi label="Live (last 60s)" value={liveCount} />
        <Kpi label="Running now" value={totalRunning} />
      </div>

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium">Status</th>
              <th className="px-4 py-2 font-medium">Host</th>
              <th className="px-4 py-2 font-medium">Environment</th>
              <th className="px-4 py-2 font-medium">Instance</th>
              <th className="px-4 py-2 font-medium">Last seen</th>
              <th className="px-4 py-2 font-medium">Running</th>
              <th className="px-4 py-2 font-medium">Total runs</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {hosts.map((h) => {
              const f = freshness(h.lastSeen);
              return (
                <tr key={h.instanceId}>
                  <td className="px-4 py-2">
                    <span className={`inline-flex items-center gap-2 ${f.text}`}>
                      <span className={`inline-flex rounded-full h-2 w-2 ${f.dot}`} />
                      {f.label}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-fg">{h.name ?? "—"}</td>
                  <td className="px-4 py-2 text-fg-2">
                    {h.environment ?? "—"}
                  </td>
                  <td
                    className="px-4 py-2 text-muted font-mono text-xs"
                    title={h.instanceId}
                  >
                    {h.instanceId.slice(0, 12)}
                  </td>
                  <td className="px-4 py-2 text-fg-2" title={h.lastSeen}>
                    {ago(h.lastSeen)}
                  </td>
                  <td className="px-4 py-2 text-fg-2">
                    {h.currentlyRunning}
                  </td>
                  <td className="px-4 py-2 text-fg-2">
                    {h.totalExecutions.toLocaleString()}
                  </td>
                </tr>
              );
            })}
            {hosts.length === 0 && (
              <tr>
                <td colSpan={7} className="px-4 py-8 text-center text-muted">
                  No hosts have executed trains yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function Kpi({ label, value }: { label: string; value: number }) {
  return (
    <div className="bg-surface rounded-lg border border-line p-4">
      <p className="text-xs text-muted">{label}</p>
      <p className="text-2xl font-bold text-fg">{value}</p>
    </div>
  );
}
