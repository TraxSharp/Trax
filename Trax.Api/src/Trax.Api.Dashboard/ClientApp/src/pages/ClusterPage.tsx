import { Link } from "react-router-dom";
import { useQuery } from "urql";
import { HOSTS, SCHEDULER_CONFIG, SERVER_PROCESS } from "../graphql/queries";
import { useAnswers } from "../lib/answerable";
import { usePoll } from "../lib/poll";
import type { HostInfo } from "../types";

// Each host rewrites its heartbeat row every 15 seconds; the server calls it offline after three
// missed beats (45 seconds).
function status(online: boolean): { label: string; dot: string; text: string } {
  return online
    ? { label: "Online", dot: "bg-ok", text: "text-ok-fg" }
    : { label: "Offline", dot: "bg-idle", text: "text-muted" };
}

/** What the host runs besides trains: the scheduler's two loops, by the names the Scheduler page uses. */
function roles(h: HostInfo): string {
  const parts = [
    h.runsManifestManager ? "Manifest manager" : null,
    h.runsDispatcher ? "Job dispatcher" : null,
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

function formatBytes(bytes: number): string {
  const mb = bytes / (1024 * 1024);
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(0)} MB`;
}

function formatUptime(seconds: number): string {
  const s = Math.floor(seconds);
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}

interface HostsData {
  operations: { hosts: HostInfo[] };
}

interface ProcessData {
  operations: {
    metrics: {
      server: { uptimeSeconds: number; workingSetBytes: number; gcHeapBytes: number };
      serverCpuPercent: number | null;
    };
  };
}

export function ClusterPage() {
  const [result, reexecute] = useQuery<HostsData>({ query: HOSTS });
  usePoll(() => reexecute({ requestPolicy: "network-only" }));

  const hosts = result.data?.operations?.hosts ?? [];
  const totalRunning = hosts.reduce((n, h) => n + h.currentlyRunning, 0);
  const onlineCount = hosts.filter((h) => h.online).length;
  // A host reports the dispatcher only while it is switched on, so a paused dispatcher shows as no
  // host running it; say which it is.
  const [{ data: config }] = useQuery<{ operations: { config: { scheduler: { jobDispatcherEnabled: boolean } } } }>({
    query: SCHEDULER_CONFIG,
  });
  const paused = config?.operations?.config?.scheduler?.jobDispatcherEnabled === false;
  const noDispatcher = !paused && result.data != null && !hosts.some((h) => h.online && h.runsDispatcher);

  return (
    <div>
      <div className="flex items-center justify-between mb-2 gap-4">
        <h1 className="text-2xl font-bold text-fg">Hosts</h1>
        <button
          onClick={() => reexecute({ requestPolicy: "network-only" })}
          className="text-sm px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 hover:bg-hover"
        >
          Refresh
        </button>
      </div>

      <p className="text-sm text-muted mb-6">
        Every process that runs Trax beats every 15 seconds, with or without work. A host that misses three beats
        is offline; one silent for a day is no longer listed.
      </p>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6">
        <Kpi label="Hosts" value={hosts.length} />
        <Kpi label="Online now" value={onlineCount} />
        <Kpi label="Running now" value={totalRunning} />
      </div>

      {paused && (
        <div role="status" className="bg-warn-soft border border-warn-line text-warn-fg rounded-lg p-4 text-sm mb-4">
          The job dispatcher is paused, so no host starts queued work.{" "}
          <Link to="/system/scheduler" className="underline">
            Resume it on the Scheduler page
          </Link>
          .
        </div>
      )}

      {noDispatcher && (
        <div role="status" className="bg-warn-soft border border-warn-line text-warn-fg rounded-lg p-4 text-sm mb-4">
          No online host runs the job dispatcher, so queued work is not being started. Start a host that runs the
          scheduler, or see{" "}
          <a
            href="https://traxsharp.net/docs/scheduler/setup"
            target="_blank"
            rel="noreferrer"
            className="underline"
          >
            setting up the scheduler
          </a>
          .
        </div>
      )}

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-x-auto mb-6">
        <table className="w-full text-sm" aria-label="Hosts">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium">Host</th>
              <th className="px-4 py-2 font-medium">Status</th>
              <th className="px-4 py-2 font-medium">Runs the</th>
              <th className="px-4 py-2 font-medium">Worker slots busy</th>
              <th className="px-4 py-2 font-medium">Running now</th>
              <th className="px-4 py-2 font-medium">Last heartbeat</th>
              <th className="px-4 py-2 font-medium">Environment</th>
              <th className="px-4 py-2 font-medium">Version</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {hosts.map((h) => {
              const f = status(h.online);
              return (
                <tr key={h.instanceId}>
                  <td className="px-4 py-2 text-fg">
                    {h.name}
                    <span className="block text-muted font-mono text-xs" title={h.instanceId}>
                      {h.instanceId.slice(0, 12)}
                    </span>
                  </td>
                  <td className="px-4 py-2">
                    <span className={`inline-flex items-center gap-2 ${f.text}`}>
                      <span className={`inline-flex rounded-full h-2 w-2 ${f.dot}`} />
                      {f.label}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-fg-2">{roles(h)}</td>
                  <td className="px-4 py-2 text-fg-2">
                    {h.workerSlots === null ? "No local workers" : `${h.workerSlotsInUse ?? 0} of ${h.workerSlots}`}
                  </td>
                  <td className="px-4 py-2 text-fg-2">
                    {h.currentlyRunning > 0 ? (
                      <Link
                        to={`/runs?state=IN_PROGRESS&host=${encodeURIComponent(h.name)}`}
                        className="text-accent-fg hover:underline"
                        title={`Runs in progress on ${h.name}`}
                      >
                        {h.currentlyRunning}
                      </Link>
                    ) : (
                      0
                    )}
                  </td>
                  <td className="px-4 py-2 text-fg-2" title={h.lastHeartbeat}>
                    {ago(h.lastHeartbeat)}
                  </td>
                  <td className="px-4 py-2 text-fg-2">{h.environment}</td>
                  <td className="px-4 py-2 text-muted text-xs">{h.traxVersion}</td>
                </tr>
              );
            })}
            {result.data && hosts.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-muted">
                  No host has sent a heartbeat in the last day. A host appears here once it starts with Trax
                  registered.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <ThisProcess />
    </div>
  );
}

// The process serving this page: the only host whose memory and CPU the dashboard can read. The
// demo's recordings do not hold this read yet, so it is not offered there (lib/answerable.ts).
function ThisProcess() {
  const answered = useAnswers("ServerProcess");
  const [{ data, error }, reexecute] = useQuery<ProcessData>({ query: SERVER_PROCESS, pause: !answered });
  usePoll(() => {
    if (answered) reexecute({ requestPolicy: "network-only" });
  });
  if (!answered) return null;
  const m = data?.operations?.metrics;
  return (
    <section aria-labelledby="this-process" className="bg-surface rounded-lg border border-line p-5">
      <h2 id="this-process" className="text-sm font-semibold text-fg mb-1">
        This dashboard&apos;s process
      </h2>
      <p className="text-xs text-muted mb-3">The process serving the API and this console. Other hosts report only their heartbeat.</p>
      {error && <p className="text-sm text-danger-fg">{error.message}</p>}
      {m && (
        <dl className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
          <Stat label="CPU" value={m.serverCpuPercent == null ? "—" : `${m.serverCpuPercent.toFixed(1)}%`} />
          <Stat label="Working set" value={formatBytes(m.server.workingSetBytes)} />
          <Stat label="GC heap" value={formatBytes(m.server.gcHeapBytes)} />
          <Stat label="Uptime" value={formatUptime(m.server.uptimeSeconds)} />
        </dl>
      )}
    </section>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="text-lg font-semibold text-fg tabular-nums">{value}</dd>
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
