import { useQuery } from "urql";
import { HOSTS } from "../graphql/queries";
import { usePoll } from "../lib/poll";
import type { HostInfo } from "../types";

// Each host rewrites its heartbeat row every 15 seconds; the server calls it offline after three
// missed beats (45 seconds).
function status(online: boolean): { label: string; dot: string; text: string } {
  return online
    ? { label: "Online", dot: "bg-ok", text: "text-ok-fg" }
    : { label: "Offline", dot: "bg-idle", text: "text-muted" };
}

function roles(h: HostInfo): string {
  const parts = [
    h.runsDispatcher ? "dispatcher" : null,
    h.runsManifestManager ? "manifest manager" : null,
  ].filter((p) => p !== null);
  return parts.length > 0 ? parts.join(", ") : "—";
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
  usePoll(() => reexecute({ requestPolicy: "network-only" }));

  const hosts = result.data?.operations?.hosts ?? [];
  const totalRunning = hosts.reduce((n, h) => n + h.currentlyRunning, 0);
  const onlineCount = hosts.filter((h) => h.online).length;
  const noDispatcher = hosts.length > 0 && !hosts.some((h) => h.online && h.runsDispatcher);

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
        Every host process beats every 15 seconds, with or without work. A host that misses three
        beats is offline; one silent for a day is no longer listed.
      </p>

      <div className="grid grid-cols-3 gap-4 mb-6">
        <Kpi label="Hosts" value={hosts.length} />
        <Kpi label="Online now" value={onlineCount} />
        <Kpi label="Running now" value={totalRunning} />
      </div>

      {noDispatcher && (
        <div className="bg-warn-soft border border-warn-line text-warn-fg rounded-lg p-4 text-sm mb-4">
          No online host runs the dispatcher, so queued work is not being started.
        </div>
      )}

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
              <th className="px-4 py-2 font-medium">Last heartbeat</th>
              <th className="px-4 py-2 font-medium">Runs</th>
              <th className="px-4 py-2 font-medium">Workers busy</th>
              <th className="px-4 py-2 font-medium">Running</th>
              <th className="px-4 py-2 font-medium">Version</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {hosts.map((h) => {
              const f = status(h.online);
              return (
                <tr key={h.instanceId}>
                  <td className="px-4 py-2">
                    <span className={`inline-flex items-center gap-2 ${f.text}`}>
                      <span className={`inline-flex rounded-full h-2 w-2 ${f.dot}`} />
                      {f.label}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-fg">{h.name}</td>
                  <td className="px-4 py-2 text-fg-2">{h.environment}</td>
                  <td
                    className="px-4 py-2 text-muted font-mono text-xs"
                    title={h.instanceId}
                  >
                    {h.instanceId.slice(0, 12)}
                  </td>
                  <td className="px-4 py-2 text-fg-2" title={h.lastHeartbeat}>
                    {ago(h.lastHeartbeat)}
                  </td>
                  <td className="px-4 py-2 text-fg-2">{roles(h)}</td>
                  <td className="px-4 py-2 text-fg-2">
                    {h.workerSlots === null ? "—" : `${h.workerSlotsInUse ?? 0} / ${h.workerSlots}`}
                  </td>
                  <td className="px-4 py-2 text-fg-2">{h.currentlyRunning}</td>
                  <td className="px-4 py-2 text-muted text-xs">{h.traxVersion}</td>
                </tr>
              );
            })}
            {hosts.length === 0 && (
              <tr>
                <td colSpan={9} className="px-4 py-8 text-center text-muted">
                  No host has sent a heartbeat in the last day.
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
