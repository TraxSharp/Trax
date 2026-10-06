import { useMemo } from "react";
import { useSubscription } from "urql";
import { ON_DATA_CHANGED, ON_TRAIN_STATE_CHANGED } from "../graphql/subscriptions";
import { StateBadge } from "../components/StateBadge";
import type { ChangeDomain, TrainLifecycleEvent, TrainState } from "../types";
import { useNow } from "../lib/useNow";

const FEED_LIMIT = 60;
const DOMAINS: ChangeDomain[] = [
  "WORK_QUEUE",
  "DEAD_LETTER",
  "MANIFEST",
  "MANIFEST_GROUP",
  "SCHEDULER_CONFIG",
];

interface DataSignal {
  domain: ChangeDomain;
  timestamp: string;
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}

export function RealTimePage() {
  // Accumulate both streams. Lifecycle events drive the feed; data-change signals drive the
  // per-domain counters. Both are diagnostics for "is real-time healthy and what's flowing?".
  const [{ data: lifecycle }] = useSubscription<
    { onTrainStateChanged: TrainLifecycleEvent },
    TrainLifecycleEvent[]
  >({ query: ON_TRAIN_STATE_CHANGED }, (prev = [], data) =>
    [data.onTrainStateChanged, ...prev].slice(0, FEED_LIMIT),
  );

  const [{ data: signals }] = useSubscription<
    { onDataChanged: DataSignal },
    DataSignal[]
  >({ query: ON_DATA_CHANGED }, (prev = [], data) =>
    [data.onDataChanged, ...prev].slice(0, 500),
  );

  const events = lifecycle ?? [];
  const domainCounts = useMemo(() => {
    const counts = new Map<string, number>(DOMAINS.map((d) => [d, 0]));
    for (const s of signals ?? []) counts.set(s.domain, (counts.get(s.domain) ?? 0) + 1);
    return counts;
  }, [signals]);

  const now = useNow();
  const lastEventAgo =
    events.length > 0 ? now - new Date(events[0].timestamp).getTime() : null;
  const receiving = lastEventAgo != null && lastEventAgo < 10_000;

  return (
    <div>
      <div className="flex items-center gap-3 mb-6">
        <h1 className="text-2xl font-bold text-fg">Real-time</h1>
        <span className="inline-flex items-center gap-2 text-sm text-muted">
          <span className="relative flex h-2 w-2">
            {receiving && (
              <span className="absolute inline-flex h-full w-full rounded-full bg-ok opacity-75 animate-ping" />
            )}
            <span
              className={`relative inline-flex rounded-full h-2 w-2 ${
                receiving ? "bg-ok" : "bg-idle"
              }`}
            />
          </span>
          {receiving ? "Receiving events" : "Connected, idle"}
        </span>
      </div>

      <p className="text-sm text-muted mb-4">
        Live diagnostics for the subscription pipeline. Lifecycle transitions drive the feed;
        coalesced <code>onDataChanged</code> signals are counted per domain since this page opened.
      </p>

      <div className="grid grid-cols-2 md:grid-cols-5 gap-4 mb-8">
        {DOMAINS.map((d) => (
          <div
            key={d}
            className="bg-surface rounded-lg border border-line p-4"
          >
            <p className="text-xs text-muted">{d.replace(/_/g, " ")}</p>
            <p className="text-2xl font-bold text-fg">
              {domainCounts.get(d) ?? 0}
            </p>
          </div>
        ))}
      </div>

      <h2 className="text-sm font-semibold text-fg mb-3">Event stream</h2>
      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium">Time</th>
              <th className="px-4 py-2 font-medium">Train</th>
              <th className="px-4 py-2 font-medium">State</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {events.map((e, i) => (
              <tr key={`${e.metadataId}-${e.timestamp}-${i}`}>
                <td className="px-4 py-2 text-muted whitespace-nowrap">
                  {new Date(e.timestamp).toLocaleTimeString()}
                </td>
                <td className="px-4 py-2 text-fg-2" title={e.trainName}>
                  {shortName(e.trainName)}
                </td>
                <td className="px-4 py-2">
                  <StateBadge state={e.trainState as TrainState} />
                </td>
              </tr>
            ))}
            {events.length === 0 && (
              <tr>
                <td colSpan={3} className="px-4 py-8 text-center text-muted">
                  Waiting for events…
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
