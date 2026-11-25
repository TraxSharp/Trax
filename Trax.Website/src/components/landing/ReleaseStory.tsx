"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import data from "@/data/deploy-recordings.json";
import type { DeployRecordings } from "@/lib/deploy-replay";
import {
  LABELS,
  SIMULATED_CAPTION,
  STORY_MS,
  captionAt,
  counterAt,
  sourceLabel,
  stepAt,
  storyOf,
  type Story,
  type StoryKind,
  type StoryTarget,
} from "@/lib/release-story";
import { HUE, NEUTRAL, alpha } from "@/lib/hue";

// One release, as a moving picture: requests flowing to the running version, a job the release waits for, the new
// version starting beside the old one and taking over. Read off the recorded Kubernetes run of the DistributedWorkers
// sample (src/lib/release-story.ts says how); AWS is simulated and says so.
const recordings = data as unknown as DeployRecordings;
// Out and back again, in story time: about a second and a half each way, slow enough to follow one dot.
const TRIP = 3000;
const HOLD = 2500;

type Point = { x: number; y: number };
type Rect = { x: number; y: number; w: number; h: number };
interface Layout {
  view: string;
  people: Rect;
  router: Rect;
  v1: Rect;
  v2: Rect;
  /** Where the requests leave, turn and arrive. */
  P: Point;
  R: Point;
  V1: Point;
  V2: Point;
  /** The status goes beside the name, or on the line under it. */
  inline: boolean;
  /** Where "request" and "answer" are written, beside their lanes. */
  inLabel: Point;
  outLabel: Point;
  id: string;
}
// Left to right on a wide screen; top to bottom on a phone, so the words stay readable.
const WIDE: Layout = {
  view: "0 0 820 300",
  people: { x: 20, y: 115, w: 130, h: 70 },
  router: { x: 300, y: 118, w: 130, h: 64 },
  v1: { x: 590, y: 40, w: 210, h: 84 },
  v2: { x: 590, y: 176, w: 210, h: 84 },
  P: { x: 150, y: 150 },
  R: { x: 300, y: 150 },
  V1: { x: 590, y: 82 },
  V2: { x: 590, y: 218 },
  inline: true,
  inLabel: { x: 225, y: 135 },
  outLabel: { x: 225, y: 175 },
  id: "wide",
};
const TALL: Layout = {
  view: "0 0 360 400",
  people: { x: 115, y: 10, w: 130, h: 60 },
  router: { x: 115, y: 140, w: 130, h: 60 },
  v1: { x: 8, y: 280, w: 166, h: 110 },
  v2: { x: 186, y: 280, w: 166, h: 110 },
  P: { x: 180, y: 70 },
  R: { x: 180, y: 140 },
  V1: { x: 91, y: 280 },
  V2: { x: 269, y: 280 },
  inline: false,
  inLabel: { x: 218, y: 108 },
  outLabel: { x: 142, y: 108 },
  id: "tall",
};

export default function ReleaseStory() {
  const [kind, setKind] = useState<StoryKind>("normal");
  const [target, setTarget] = useState<StoryTarget>("kubernetes");
  const [at, setAt] = useState(0);
  const [playing, setPlaying] = useState(false);
  const base = useRef(0);
  const story = useMemo(() => storyOf(recordings, kind, target), [kind, target]);

  useEffect(() => {
    if (!playing) return;
    const timer = setInterval(() => {
      const t = (performance.now() - base.current) % (STORY_MS + HOLD);
      setAt(Math.min(t, STORY_MS));
    }, 40);
    return () => clearInterval(timer);
  }, [playing]);

  const play = (from: number) => {
    base.current = performance.now() - from;
    setPlaying(true);
  };
  const choose = (apply: () => void) => {
    apply();
    setAt(0);
    base.current = performance.now();
  };

  // Plays, and keeps looping, once it is on screen, unless the reader prefers less motion.
  const root = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const node = root.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting) return;
        observer.disconnect();
        base.current = performance.now();
        setPlaying(true);
      },
      { threshold: 0.3 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, []);

  if (!story) return null;
  const counter = counterAt(story, at);
  const labels = LABELS[target];

  return (
    <div ref={root} className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="flex flex-wrap gap-1.5" role="group" aria-label="Which release">
          <Pill on={kind === "normal"} onClick={() => choose(() => setKind("normal"))}>
            A normal release
          </Pill>
          <Pill on={kind === "broken"} onClick={() => choose(() => setKind("broken"))}>
            A broken release
          </Pill>
        </div>
        <div className="flex gap-1 rounded-full border border-border p-0.5" role="group" aria-label="Where it runs">
          {(["kubernetes", "aws"] as StoryTarget[]).map((t) => (
            <button
              key={t}
              onClick={() => choose(() => setTarget(t))}
              aria-pressed={target === t}
              className={`rounded-full px-3 py-1 text-sm transition-colors ${target === t ? "bg-accent/15 text-accent-bright" : "text-text-secondary hover:text-text-primary"}`}
            >
              {t === "kubernetes" ? "Kubernetes" : "AWS"}
            </button>
          ))}
        </div>
        <button
          onClick={() => (playing ? setPlaying(false) : play(at >= STORY_MS ? 0 : at))}
          className="ml-auto rounded-md border border-border bg-bg-tertiary px-3 py-1.5 text-sm text-text-primary hover:border-accent/60"
        >
          {playing ? "Pause" : "Play"}
        </button>
      </div>

      <div className="relative overflow-hidden rounded-lg border border-border bg-bg-secondary">
        <div className="flex flex-wrap items-center gap-2 px-3 pt-3 text-xs">
          <span className="font-mono text-text-secondary">{sourceLabel(story)}</span>
          {story.simulated && (
            <span className="rounded border px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wider" style={{ borderColor: HUE.amber, color: HUE.amber }}>
              Simulated
            </span>
          )}
        </div>
        <div className="hidden sm:block">
          <Picture story={story} at={at} labels={labels} l={WIDE} />
        </div>
        <div className="sm:hidden">
          <Picture story={story} at={at} labels={labels} l={TALL} />
        </div>
        <Steps story={story} at={at} />
        <div className="h-0.5 bg-border">
          <div className="h-full bg-accent/70" style={{ width: `${(at / STORY_MS) * 100}%` }} />
        </div>
      </div>

      <div className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1">
        <p className="min-h-[3em] max-w-2xl text-text-primary sm:min-h-[1.5em]" aria-live="polite">
          {captionAt(story, at)}
        </p>
        {counter ? (
          <p className="font-mono text-sm">
            <span style={{ color: HUE.green }}>requests answered {counter.answered}</span>
            <span className="text-text-muted"> · </span>
            <span style={{ color: counter.failed ? HUE.redSoft : undefined }} className={counter.failed ? "" : "text-text-secondary"}>
              failed {counter.failed}
            </span>
          </p>
        ) : (
          <p className="font-mono text-sm text-text-muted">no requests measured</p>
        )}
      </div>
      <p className="text-xs leading-relaxed text-text-muted">
        {story.simulated ? (
          <span style={{ color: HUE.amber }}>{SIMULATED_CAPTION}</span>
        ) : (
          <>
            Recorded with trax deploy on Kubernetes (k3s) and the DistributedWorkers sample: a request to the app every 0.2 seconds, one dot drawn for every{" "}
            {story.perDot}, and about {Math.round((story.scenario?.duration ?? 0) / 1000)} seconds shown in {STORY_MS / 1000}.
            {story.job && " The running job is a finished run the recorder marked as running, then ended by hand."}
          </>
        )}
      </p>
    </div>
  );
}

function Pill({ on, onClick, children }: { on: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      aria-pressed={on}
      className={`rounded-md border px-3 py-1.5 text-sm transition-colors ${on ? "border-accent bg-accent/10 text-accent-bright" : "border-border bg-bg-primary text-text-secondary hover:border-accent/60"}`}
    >
      {children}
    </button>
  );
}

/** A point along a path of points, `f` from 0 to 1. */
function along(points: { x: number; y: number }[], f: number) {
  const lengths = points.slice(1).map((p, i) => Math.hypot(p.x - points[i].x, p.y - points[i].y));
  let d = Math.max(0, Math.min(1, f)) * lengths.reduce((a, b) => a + b, 0);
  for (let i = 0; i < lengths.length; i++) {
    if (d <= lengths[i]) {
      const k = lengths[i] ? d / lengths[i] : 0;
      return { x: points[i].x + (points[i + 1].x - points[i].x) * k, y: points[i].y + (points[i + 1].y - points[i].y) * k };
    }
    d -= lengths[i];
  }
  return points[points.length - 1];
}

// How far each lane sits from the line between two boxes.
const LANE = -9;

/** A path moved sideways by `d`, to the left of the way it runs: going one way and coming back sit on either side. */
function offset(points: Point[], d: number): Point[] {
  const shifted = points.slice(1).map((b, i) => {
    const a = points[i];
    const len = Math.hypot(b.x - a.x, b.y - a.y) || 1;
    const nx = (-(b.y - a.y) / len) * d;
    const ny = ((b.x - a.x) / len) * d;
    return [
      { x: a.x + nx, y: a.y + ny },
      { x: b.x + nx, y: b.y + ny },
    ];
  });
  const out = [shifted[0][0]];
  for (let i = 0; i < shifted.length - 1; i++)
    out.push({ x: (shifted[i][1].x + shifted[i + 1][0].x) / 2, y: (shifted[i][1].y + shifted[i + 1][0].y) / 2 });
  out.push(shifted[shifted.length - 1][1]);
  return out;
}

/** One lane: requests toward the app, or answers back to the people, with an arrow at its end. */
function Lane({ points, kind, id, opacity = 1, dashed = false }: { points: Point[]; kind: "in" | "out"; id: string; opacity?: number; dashed?: boolean }) {
  const lane = offset(kind === "in" ? points : [...points].reverse(), LANE);
  return (
    <polyline
      points={lane.map((p) => `${p.x},${p.y}`).join(" ")}
      fill="none"
      stroke={alpha(kind === "in" ? HUE.blue : HUE.green, 45)}
      strokeWidth={1.5}
      strokeDasharray={dashed ? "4 5" : undefined}
      markerEnd={`url(#${id}-${kind})`}
      opacity={opacity}
    />
  );
}

/** The deploy's steps, each lit from the moment the recording shows it start, with a line under the current one. */
function Steps({ story, at }: { story: Story; at: number }) {
  const current = stepAt(story, at);
  return (
    <div className="border-t border-border px-3 py-2.5">
      <ol className="flex flex-wrap gap-1.5" aria-label="The deploy's steps">
        {story.steps.map((step, i) => {
          const state = i < current ? "done" : i === current ? "now" : "ahead";
          const bad = step.key === "never" || step.key === "back";
          const color = bad ? HUE.redSoft : HUE.green;
          return (
            <li
              key={step.key}
              className="flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs transition-colors duration-300"
              style={
                state === "ahead"
                  ? { borderColor: NEUTRAL.line, color: NEUTRAL.muted }
                  : { borderColor: alpha(color, state === "now" ? 80 : 40), color, background: state === "now" ? alpha(color, 14) : undefined }
              }
            >
              <span className="font-mono text-[10px] opacity-70">{i + 1}</span>
              {step.label}
            </li>
          );
        })}
      </ol>
      <p className="mt-1.5 min-h-[1.5em] text-xs text-text-secondary">{current >= 0 ? story.steps[current].line : ""}</p>
    </div>
  );
}

const between = (at: number, from: number | null, to: number | null) => from != null && at >= from && (to == null || at < to);

function Picture({ story, at, labels, l }: { story: Story; at: number; labels: (typeof LABELS)[StoryTarget]; l: Layout }) {
  const v1Stopping = at >= story.v1.readyUntil;
  const v1Opacity = story.v1.goneAt != null && at >= story.v1.goneAt ? 0.12 : v1Stopping ? 0.45 : 1;
  const v2Shown = at >= story.v2.from && (story.v2.goneAt == null || at < story.v2.goneAt + 900);
  const v2Ready = story.v2.readyAt != null && at >= story.v2.readyAt;
  const v2Leaving = story.v2.goneAt != null && at >= story.v2.goneAt;
  const crashing = story.v2.crashes.some((c) => at >= c && at < c + 700) || (story.v2.crashes.length > 0 && at >= story.v2.crashes[0] && !v2Ready && !v2Leaving);
  const v2Color = v2Ready ? HUE.green : crashing ? HUE.redSoft : HUE.amber;
  const waiting = story.waiting && between(at, story.waiting.from, story.waiting.to) && at < story.v2.from;
  const jobRunning = story.job && at < story.job.to;
  const jobFill = story.job ? Math.min(1, Math.max(0, (at - story.job.from) / Math.max(1, story.job.to - story.job.from))) : 0;
  const { P, R, V1, V2 } = l;
  // The name, and its status beside it or under it.
  const head = (box: Rect, name: string, status: string) => (
    <>
      <text x={box.x + 14} y={box.y + 26} className="fill-text-primary" fontSize={16} fontWeight={600}>
        {name}
      </text>
      <text x={l.inline ? box.x + 44 : box.x + 14} y={l.inline ? box.y + 26 : box.y + 46} className="fill-text-muted" fontSize={12}>
        {status}
      </text>
    </>
  );

  return (
    <svg viewBox={l.view} className="mx-auto block h-auto w-full max-w-[860px]" role="img" aria-label="Requests going to the running version while a new version takes over">
      <defs>
        {(["in", "out"] as const).map((k) => (
          <marker key={k} id={`${l.id}-${k}`} viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M0 0 L10 5 L0 10 z" fill={k === "in" ? HUE.blue : HUE.green} />
          </marker>
        ))}
      </defs>
      {/* Requests go in on one lane and answers come back on the other, each with its arrow. */}
      <Lane points={[P, R, V1]} kind="in" id={l.id} opacity={v1Opacity} />
      <Lane points={[P, R, V1]} kind="out" id={l.id} opacity={v1Opacity} />
      {v2Shown && <Lane points={[P, R, V2]} kind="in" id={l.id} dashed={!v2Ready} />}
      {v2Shown && <Lane points={[P, R, V2]} kind="out" id={l.id} dashed={!v2Ready} />}
      <text x={l.inLabel.x} y={l.inLabel.y} fontSize={11} textAnchor="middle" style={{ fill: HUE.blue }}>
        request
      </text>
      <text x={l.outLabel.x} y={l.outLabel.y} fontSize={11} textAnchor="middle" style={{ fill: HUE.green }}>
        answer
      </text>

      <Node box={l.people} title="People" sub="using the app" />
      <Node box={l.router} title="Traffic" sub={labels.router} />

      <g opacity={v1Opacity} style={{ transition: "opacity 600ms" }}>
        <rect x={l.v1.x} y={l.v1.y} width={l.v1.w} height={l.v1.h} rx={8} fill={alpha(HUE.green, 10)} stroke={HUE.green} strokeWidth={1.5} />
        {head(l.v1, "v1", `${v1Stopping ? "stopping" : "running"}, ${labels.copy}`)}
        {story.job && (
          <g opacity={jobRunning ? 1 : 0.5}>
            <rect x={l.v1.x + 14} y={l.v1.y + l.v1.h - 36} width={l.v1.w - 28} height={22} rx={4} fill={NEUTRAL.line} opacity={0.5} />
            <rect x={l.v1.x + 14} y={l.v1.y + l.v1.h - 36} width={(l.v1.w - 28) * jobFill} height={22} rx={4} fill={alpha(HUE.amber, 55)} />
            <text x={l.v1.x + 22} y={l.v1.y + l.v1.h - 21} className="fill-text-primary" fontSize={11.5}>
              {jobRunning ? labels.job : "the job finished"}
            </text>
          </g>
        )}
      </g>

      {waiting && (
        <g>
          <rect x={l.v2.x} y={l.v2.y} width={l.v2.w} height={l.v2.h} rx={8} fill="none" stroke={HUE.violet} strokeWidth={1.5} strokeDasharray="5 5" />
          <text x={l.v2.x + 14} y={l.v2.y + 30} fontSize={13} style={{ fill: HUE.violet }}>
            v2 is ready to ship
          </text>
          <text x={l.v2.x + 14} y={l.v2.y + 50} className="fill-text-muted" fontSize={11.5}>
            {labels.wait}
          </text>
          <text x={l.v2.x + 14} y={l.v2.y + 66} className="fill-text-muted" fontSize={11.5}>
            for the job
          </text>
        </g>
      )}
      {v2Shown && (
        <g opacity={v2Leaving ? 0 : 1} style={{ transition: "opacity 800ms" }}>
          <rect x={l.v2.x} y={l.v2.y} width={l.v2.w} height={l.v2.h} rx={8} fill={alpha(v2Color, 10)} stroke={v2Color} strokeWidth={1.5} strokeDasharray={v2Ready ? undefined : "5 4"} />
          {head(l.v2, "v2", `${v2Ready ? "ready" : crashing ? "failing" : "starting"}, ${labels.copy}`)}
          <text x={l.v2.x + 14} y={l.v2.y + l.v2.h - 20} fontSize={12} style={{ fill: v2Color }}>
            {v2Ready ? "takes requests" : crashing ? "never gets ready" : "no requests yet"}
          </text>
        </g>
      )}

      {story.dots.map((d, i) => {
        const f = (at - (d.at - TRIP / 2)) / TRIP;
        if (f < 0 || f > 1) return null;
        const target = d.to === "v1" ? V1 : V2;
        const going = f < 0.5;
        const p = going ? along(offset([P, R, target], LANE), f * 2) : along(offset([target, R, P], LANE), (f - 0.5) * 2);
        return <circle key={i} cx={p.x} cy={p.y} r={5} fill={going ? HUE.blue : d.ok ? HUE.green : HUE.redSoft} />;
      })}
    </svg>
  );
}

function Node({ box, title, sub }: { box: Rect; title: string; sub: string }) {
  return (
    <g>
      <rect x={box.x} y={box.y} width={box.w} height={box.h} rx={8} fill="var(--bg-primary)" stroke={NEUTRAL.lineStrong} strokeWidth={1.2} />
      <text x={box.x + box.w / 2} y={box.y + box.h / 2 - 3} textAnchor="middle" className="fill-text-primary" fontSize={14} fontWeight={600}>
        {title}
      </text>
      <text x={box.x + box.w / 2} y={box.y + box.h / 2 + 15} textAnchor="middle" className="fill-text-muted" fontSize={12}>
        {sub}
      </text>
    </g>
  );
}
