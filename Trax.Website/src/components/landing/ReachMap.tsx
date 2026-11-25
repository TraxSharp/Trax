"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import data from "@/data/deploy-recordings.json";
import type { DeployRecordings } from "@/lib/deploy-replay";
import { reachEdges, reachScenario, type NodeKey, type ReachEdge } from "@/lib/reach-map";
import { HUE, NEUTRAL, alpha } from "@/lib/hue";

// Who can talk to whom once the DistributedWorkers sample is deployed on Kubernetes: each line lights green or red
// as the recorded checks found it (src/lib/reach-map.ts reads them off deploy-recordings.json), in the order they ran.
const recordings = data as unknown as DeployRecordings;
const STEP = 1300;
const HOLD = 4000;

const LABELS: Record<NodeKey, { title: string; sub: string }> = {
  app: { title: "Your app", sub: "the hub and its workers" },
  database: { title: "Postgres", sub: "its database" },
  rabbitmq: { title: "RabbitMQ", sub: "its broker" },
  other: { title: "Another pod", sub: "in the cluster, not the app's" },
  internet: { title: "The internet", sub: "any name it did not list" },
  metadata: { title: "Cloud metadata", sub: "169.254.169.254" },
};

type Layout = { view: string; at: Record<NodeKey, { x: number; y: number }>; w: number; h: number; mark: number };
const WIDE: Layout = {
  view: "0 0 820 330",
  mark: 0.4,
  w: 170,
  h: 58,
  at: {
    internet: { x: 210, y: 45 },
    metadata: { x: 610, y: 45 },
    app: { x: 210, y: 175 },
    database: { x: 610, y: 165 },
    rabbitmq: { x: 610, y: 285 },
    other: { x: 210, y: 295 },
  },
};
const TALL: Layout = {
  view: "0 0 360 500",
  mark: 0.55,
  w: 150,
  h: 56,
  at: {
    internet: { x: 90, y: 40 },
    metadata: { x: 270, y: 40 },
    app: { x: 90, y: 200 },
    database: { x: 270, y: 200 },
    rabbitmq: { x: 270, y: 365 },
    other: { x: 90, y: 460 },
  },
};

export default function ReachMap() {
  const scenario = reachScenario(recordings);
  const edges = useMemo(() => (scenario ? [...reachEdges(scenario)].sort((a, b) => a.at - b.at) : []), [scenario]);
  const [shownCount, setShownCount] = useState(edges.length);
  const [playing, setPlaying] = useState(false);
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!playing) return;
    let i = 0;
    const timer = setInterval(() => {
      i = (i + 1) % (edges.length + Math.ceil(HOLD / STEP) + 1);
      setShownCount(Math.min(i, edges.length));
    }, STEP);
    return () => clearInterval(timer);
  }, [playing, edges.length]);

  useEffect(() => {
    const node = root.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting) return;
        observer.disconnect();
        setShownCount(0);
        setPlaying(true);
      },
      { threshold: 0.3 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, []);

  if (!scenario) return null;
  const latest = shownCount > 0 ? edges[shownCount - 1] : null;

  return (
    <div ref={root} className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-text-secondary">
          Checked on the recorded deploy, on Kubernetes (k3s): green is a connection that opened, red one that was refused.
        </p>
        <button
          onClick={() => {
            if (playing) {
              setPlaying(false);
              setShownCount(edges.length);
            } else {
              setShownCount(0);
              setPlaying(true);
            }
          }}
          className="flex-none rounded-md border border-border bg-bg-tertiary px-3 py-1.5 text-sm text-text-primary hover:border-accent/60"
        >
          {playing ? "Pause" : "Play"}
        </button>
      </div>
      <div className="rounded-lg border border-border bg-bg-secondary p-2">
        <div className="hidden sm:block">
          <Map edges={edges} shown={shownCount} l={WIDE} />
        </div>
        <div className="sm:hidden">
          <Map edges={edges} shown={shownCount} l={TALL} />
        </div>
      </div>
      <p className="min-h-[3em] text-sm text-text-primary sm:min-h-[1.5em]" aria-live="polite">
        {latest ? describe(latest) : " "}
      </p>
      <p className="text-xs leading-relaxed text-text-muted">
        On your own machine (podman) there are no network policies; the recorded checks for it, a container outside the app reaching its
        Postgres among them, are in the fold below with every other check.
      </p>
    </div>
  );
}

/** One edge in words: who tried, what happened, and the recorded checks it rests on. */
function describe(e: ReachEdge): string {
  const a = LABELS[e.from].title;
  const b = LABELS[e.to].title;
  const verdict = e.result === "open" ? `${a} reaches ${b}.` : e.result === "refused" ? `${a} cannot reach ${b}.` : `${a} to ${b}: the checks disagree.`;
  return `${verdict} Recorded: ${e.sources.join("; ")}.`;
}

function Map({ edges, shown, l }: { edges: ReachEdge[]; shown: number; l: Layout }) {
  return (
    <svg viewBox={l.view} className="mx-auto block h-auto w-full max-w-[860px]" role="img" aria-label="Which parts can reach which, as the recorded checks found">
      {edges.map((e, i) => {
        const a = l.at[e.from];
        const b = l.at[e.to];
        const lit = i < shown;
        const color = e.result === "open" ? HUE.green : e.result === "refused" ? HUE.redSoft : HUE.amber;
        // The mark sits off the middle, by the layout's own fraction, so two lines that cross never share one.
        const mid = { x: a.x + (b.x - a.x) * l.mark, y: a.y + (b.y - a.y) * l.mark };
        return (
          <g key={e.key} style={{ transition: "opacity 400ms" }} opacity={lit ? 1 : 0.35}>
            <line
              x1={a.x}
              y1={a.y}
              x2={b.x}
              y2={b.y}
              stroke={lit ? color : NEUTRAL.lineStrong}
              strokeWidth={lit ? 2.5 : 1.5}
              strokeDasharray={lit && e.result === "refused" ? "6 5" : undefined}
            />
            {lit && (
              <g>
                <circle cx={mid.x} cy={mid.y} r={11} fill="var(--bg-secondary)" stroke={color} strokeWidth={1.5} />
                <text x={mid.x} y={mid.y + 4.5} textAnchor="middle" fontSize={13} fontWeight={700} style={{ fill: color }}>
                  {e.result === "open" ? "✓" : "✕"}
                </text>
              </g>
            )}
          </g>
        );
      })}
      {(Object.keys(LABELS) as NodeKey[]).map((k) => {
        const p = l.at[k];
        return (
          <g key={k}>
            <rect x={p.x - l.w / 2} y={p.y - l.h / 2} width={l.w} height={l.h} rx={8} fill="var(--bg-primary)" />
            <rect
              x={p.x - l.w / 2}
              y={p.y - l.h / 2}
              width={l.w}
              height={l.h}
              rx={8}
              fill={k === "app" ? alpha(HUE.green, 8) : "none"}
              stroke={k === "app" ? HUE.green : NEUTRAL.lineStrong}
              strokeWidth={k === "app" ? 1.8 : 1.2}
            />
            <text x={p.x} y={p.y - 3} textAnchor="middle" fontSize={14} fontWeight={600} className="fill-text-primary">
              {LABELS[k].title}
            </text>
            <text x={p.x} y={p.y + 15} textAnchor="middle" fontSize={11.5} className="fill-text-muted">
              {LABELS[k].sub}
            </text>
          </g>
        );
      })}
    </svg>
  );
}
