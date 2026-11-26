"use client";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import CodeFold from "./CodeFold";
import { Box } from "./Diagram";
import ReplayToolbar, { PrimaryButton } from "./ReplayToolbar";
import StepButtons from "./StepButtons";
import data from "@/data/deploy-recordings.json";
import {
  SCENARIO_KEYS,
  captionAt,
  clock,
  healthAt,
  lanesOf,
  litLinesOf,
  momentsOf,
  summaryOf,
  runAt,
  type Bar,
  type DeployRecordings,
  type DeployScenario,
  type Lane,
  type ProcessState,
  type ScenarioKey,
  type TargetKey,
} from "@/lib/deploy-replay";
import { nextStop, previousStop, stepIndex, stopsOf } from "@/lib/replay-steps";
import { HUE, NEUTRAL, alpha } from "@/lib/hue";

// Real deploys, recorded by Trax.Samples' scripts/recordings/deploy.sh on a throwaway VM (k3s, and podman under
// systemd) and written here by deploy.mjs: what trax deploy printed, every pod or container of the app, a health
// probe five times a second, and the run a release has to wait for. Re-record a shape with
// `deploy.sh <shape> <kubernetes|podman>` on such a machine, then `node deploy.mjs --copy-to
// ../../../Trax.Website/src/data`; deploy/README.md there says how to make the machine.
const recordings = data as unknown as DeployRecordings;

type Target = TargetKey | "aws";

interface Shape {
  key: string;
  name: string;
  line: string;
  /** The processes, left to right; several in one entry are one process holding them all. */
  boxes: string[][];
  /** Why there is nothing to replay, when trax deploy cannot deploy this shape. */
  notDeployable?: string;
}

// The five ways an app can be split into processes, and which recording plays for each.
const SHAPES: Shape[] = [
  { key: "all-in-one", name: "All-in-one", line: "API, scheduler and the jobs they run in one process.", boxes: [["API", "Scheduler", "Jobs"]] },
  { key: "api-scheduler", name: "API + Scheduler", line: "An API process and a scheduler process, on one database.", boxes: [["API"], ["Scheduler"]] },
  { key: "hub-workers", name: "Hub + Workers", line: "A hub serves the API and schedules; workers take the jobs and run them.", boxes: [["Hub"], ["Worker"], ["Worker"]] },
  {
    key: "lambda",
    name: "Ephemeral dispatch (Lambda)",
    line: "The API hands each run to an AWS Lambda function, which runs it and stops.",
    boxes: [["API"], ["Lambda"]],
    notDeployable: "trax deploy does not deploy a Lambda function yet, so there is no deploy of this shape to replay.",
  },
  { key: "scheduler", name: "Just the scheduler", line: "One process runs workflows on a schedule, with no API of your own.", boxes: [["Scheduler"]] },
];

const TARGETS: { key: Target; label: string }[] = [
  { key: "kubernetes", label: "Kubernetes" },
  { key: "podman", label: "Your machine" },
  { key: "aws", label: "AWS" },
];
const AWS_NOTE = "Not recorded: there is no AWS account to record against. The same file deploys there through an aws environment.";

const SCENARIO_NAMES: Record<ScenarioKey, string> = {
  "first-deploy": "First deploy",
  "release-while-busy": "Release while busy",
  "broken-release": "Broken release",
  "who-can-reach-what": "Who can reach what",
};

const STATE_COLOR: Record<ProcessState, string> = {
  starting: HUE.slate,
  running: HUE.amber,
  ready: HUE.green,
  crashing: HUE.redSoft,
  stopping: NEUTRAL.lineStrong,
  gone: NEUTRAL.lineStrong,
  done: HUE.blue,
  failed: HUE.redSoft,
};
const STATE_TEXT: Record<ProcessState, string> = {
  starting: "starting",
  running: "running",
  ready: "ready",
  crashing: "crashing",
  stopping: "stopping",
  gone: "gone",
  done: "finished",
  failed: "failed",
};

// A scenario plays in under a minute: it is sped up by a whole factor, and the clock shows the recording's own time.
const PLAY_MS = 50000;
const speedOf = (s: DeployScenario | undefined) => (s ? Math.max(1, Math.ceil(s.duration / PLAY_MS)) : 1);

export default function DeployReplay() {
  const [shapeKey, setShapeKey] = useState("all-in-one");
  const [target, setTarget] = useState<Target>("kubernetes");
  const [scenarioKey, setScenarioKey] = useState<ScenarioKey>("release-while-busy");
  const [elapsed, setElapsed] = useState(0);
  const [playing, setPlaying] = useState(false);
  const base = useRef(0);

  const shape = SHAPES.find((s) => s.key === shapeKey) ?? SHAPES[0];
  const recorded = recordings.shapes[shapeKey];
  const targetData = target === "aws" ? undefined : recorded?.targets[target];
  const scenario = targetData?.scenarios[scenarioKey];
  const duration = scenario?.duration ?? 0;
  const speed = speedOf(scenario);
  const now = Math.min(elapsed, duration);
  const stops = useMemo(() => (scenario ? stopsOf(momentsOf(scenario)) : []), [scenario]);

  useEffect(() => {
    if (!playing) return;
    const timer = setInterval(() => {
      const t = (performance.now() - base.current) * speed;
      setElapsed(Math.min(t, duration));
      if (t >= duration) setPlaying(false);
    }, 50);
    return () => clearInterval(timer);
  }, [playing, speed, duration]);

  const play = useCallback(
    (from: number) => {
      base.current = performance.now() - from / speed;
      setElapsed(from);
      setPlaying(true);
    },
    [speed],
  );
  const seek = (t: number) => {
    autoplayed.current = true;
    setPlaying(false);
    setElapsed(Math.max(0, Math.min(duration, t)));
  };
  const choose = (apply: () => void) => {
    autoplayed.current = true;
    apply();
    setPlaying(false);
    setElapsed(0);
  };

  // Play once, the first time the replay is on screen, unless the reader prefers less motion.
  const root = useRef<HTMLDivElement>(null);
  const autoplayed = useRef(false);
  useEffect(() => {
    const node = root.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting || autoplayed.current) return;
        autoplayed.current = true;
        observer.disconnect();
        play(0);
      },
      { threshold: 0.25 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [play]);

  const unavailable = shape.notDeployable ?? (target === "aws" ? AWS_NOTE : !scenario ? "Not recorded yet." : null);

  return (
    <div ref={root} className="flex flex-col gap-4">
      <ShapeRow selected={shapeKey} onSelect={(k) => choose(() => setShapeKey(k))} />

      <ReplayToolbar
        caption={scenario && !unavailable && elapsed > 0 ? captionAt(scenario, now) : null}
        hint="Pick a shape, where it runs and what happens, then press Play, or step with Back and Next."
      >
        <div className="flex flex-wrap gap-1.5" role="group" aria-label="What happens">
          {SCENARIO_KEYS.map((k) => (
            <Pill key={k} on={k === scenarioKey} onClick={() => choose(() => setScenarioKey(k))}>
              {SCENARIO_NAMES[k]}
            </Pill>
          ))}
        </div>
        <div className="flex flex-wrap gap-1.5" role="group" aria-label="Where it runs">
          {TARGETS.map((t) => (
            <Pill key={t.key} round on={t.key === target} onClick={() => choose(() => setTarget(t.key))}>
              {t.label}
            </Pill>
          ))}
        </div>
      </ReplayToolbar>

      {unavailable || !scenario || !recorded ? (
        <div className="rounded-lg border border-dashed border-border bg-bg-secondary px-4 py-10 text-center text-sm text-text-muted">{unavailable}</div>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-border bg-bg-secondary p-3 text-sm">
            <PrimaryButton onClick={() => (playing ? setPlaying(false) : play(now >= duration ? 0 : now))}>
              {playing ? "Pause" : now >= duration && now > 0 ? "Play again" : "Play"}
            </PrimaryButton>
            <StepButtons
              onBack={() => seek(previousStop(stops, now))}
              onNext={() => seek(nextStop(stops, now))}
              at={stepIndex(stops, now)}
              of={stops.length}
            />
            <label className="flex min-w-[12rem] flex-1 items-center gap-2 text-xs text-text-muted">
              <span className="sr-only">Time in the recording</span>
              <input
                type="range"
                min={0}
                max={duration}
                step={100}
                value={now}
                onChange={(e) => seek(Number(e.target.value))}
                className="w-full accent-[var(--accent)]"
              />
              <span className="w-[6.5rem] flex-none text-right font-mono">
                {clock(now)} / {clock(duration)}
              </span>
            </label>
            <span className="text-xs text-text-muted">{speed > 1 ? `played ${speed}× faster than it ran` : "played as it ran"}</span>
          </div>

          <div className="grid gap-4 min-[1100px]:grid-cols-[minmax(0,5fr)_minmax(0,8fr)]">
            <FilePanel scenario={scenario} original={recorded.file.code} path={recorded.file.path} target={target as TargetKey} />
            <div className="flex min-w-0 flex-col gap-4">
              <Timeline scenario={scenario} now={now} target={target as TargetKey} scenarioKey={scenarioKey} />
              {scenarioKey === "who-can-reach-what" ? <Checks scenario={scenario} now={now} /> : <CliPanel scenario={scenario} now={now} />}
            </div>
          </div>

          <Summary scenario={scenario} scenarioKey={scenarioKey} target={target as TargetKey} />

          <CodeFold label="What the migration and the drain wrote, and what trax printed" pulse={scenario.jobs.filter((j) => j.t <= now).length}>
            <div className="grid gap-4 lg:grid-cols-2">
              <LogList title="trax deploy" lines={scenario.cli.filter((l) => l.t <= now).map((l) => ({ t: l.t, text: l.from === "you" ? `$ ${l.text}` : l.text }))} />
              <LogList title="The migration, drain and resume" lines={scenario.jobs.filter((j) => j.t <= now).map((j) => ({ t: j.t, text: `${j.job}: ${j.text}` }))} />
            </div>
          </CodeFold>

          <p className="text-xs text-text-muted">
            Recorded {scenario.recordedAt.slice(0, 10)} from commit {scenario.commit.slice(0, 12)} on {targetData?.versions.join(", ")}.
          </p>
        </>
      )}
    </div>
  );
}

function Pill({ on, round = false, onClick, children }: { on: boolean; round?: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      aria-pressed={on}
      className={`${round ? "rounded-full" : "rounded-md"} border px-3 py-1.5 text-sm transition-colors ${
        on ? "border-accent bg-accent/10 text-accent-bright" : "border-border bg-bg-primary text-text-secondary hover:border-accent/60"
      }`}
    >
      {children}
    </button>
  );
}

function ShapeRow({ selected, onSelect }: { selected: string; onSelect: (key: string) => void }) {
  return (
    <div className="grid gap-2 [grid-template-columns:repeat(auto-fit,minmax(170px,1fr))]" role="group" aria-label="How the app is split">
      {SHAPES.map((s) => {
        const on = s.key === selected;
        return (
          <button
            key={s.key}
            onClick={() => onSelect(s.key)}
            aria-pressed={on}
            className={`flex flex-col gap-2 rounded-lg border p-3 text-left transition-colors ${
              on ? "border-accent bg-accent/5" : "border-border/60 bg-bg-secondary/40 hover:border-accent/50"
            }`}
          >
            <span className={`text-sm font-semibold ${on ? "text-accent-bright" : "text-text-primary"}`}>{s.name}</span>
            <span className="text-xs leading-snug text-text-muted">{s.line}</span>
            <span className="mt-auto flex min-w-0 gap-1 overflow-hidden">
              {s.boxes.map((process, i) =>
                process.length > 1 ? (
                  <span key={i} className="flex min-w-0 flex-1 gap-1 rounded border border-border p-0.5">
                    {process.map((p) => (
                      <Box key={p} label={p} className="flex-1 px-1 py-0.5 [&>span]:text-[10px]" />
                    ))}
                  </span>
                ) : (
                  <Box key={i} label={process[0]} tone={s.notDeployable && i > 0 ? "muted" : "plain"} className="flex-1 px-1 py-0.5 [&>span]:text-[10px]" />
                ),
              )}
            </span>
          </button>
        );
      })}
    </div>
  );
}

function PanelTitle({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 border-b border-border px-4 py-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">
      {children}
    </div>
  );
}

// The file the scenario deployed, with the lines in play lit: what it changed from the shape's own file, the
// environment it deployed and each service.
function FilePanel({ scenario, original, path, target }: { scenario: DeployScenario; original: string; path: string; target: TargetKey }) {
  const lit = useMemo(() => new Set(litLinesOf(scenario, original, target)), [scenario, original, target]);
  const changed = scenario.file !== original;
  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>
        <span className="truncate font-mono normal-case tracking-normal text-text-secondary" title={path}>
          trax.deploy.json
        </span>
        {changed && <span className="ml-auto font-normal normal-case tracking-normal text-signal">changed for this run</span>}
      </PanelTitle>
      <pre className="overflow-x-auto py-3 font-mono text-[11px] leading-[1.65] text-text-secondary">
        {scenario.file.replace(/\n$/, "").split("\n").map((line, i) => (
          <div
            key={i}
            className="flex border-l-2 border-transparent pr-4"
            style={lit.has(i + 1) ? { borderLeftColor: HUE.green, background: alpha(HUE.green, 12) } : undefined}
          >
            <span className="mr-3 w-6 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
            <span className="whitespace-pre">{line}</span>
          </div>
        ))}
      </pre>
      {scenario.change && <p className="border-t border-border px-4 py-2 text-xs leading-snug text-signal">Changed for this run: {scenario.change}</p>}
      <p className="mt-auto border-t border-border px-4 py-2 font-mono text-[10.5px] text-text-muted [overflow-wrap:anywhere]">{path}</p>
    </div>
  );
}

const LABEL_W = "w-[4.5rem] sm:w-[6.5rem]";

function Timeline({ scenario, now, target, scenarioKey }: { scenario: DeployScenario; now: number; target: TargetKey; scenarioKey: ScenarioKey }) {
  const lanes = useMemo(() => lanesOf(scenario), [scenario]);
  const pct = (t: number) => `${(Math.max(0, Math.min(t, scenario.duration)) / scenario.duration) * 100}%`;
  return (
    <div className="flex min-w-0 flex-col rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>
        {target === "kubernetes" ? "Pods" : "Containers"}
        <span className="ml-auto flex gap-3 font-normal normal-case tracking-normal">
          {(["starting", "running", "ready", "crashing", "done"] as ProcessState[]).map((s) => (
            <span key={s} className="flex items-center gap-1">
              <span className="inline-block size-2 rounded-sm" style={{ background: STATE_COLOR[s] }} />
              {STATE_TEXT[s]}
            </span>
          ))}
        </span>
      </PanelTitle>
      <div className="flex flex-col gap-1.5 p-3">
        {lanes.map((lane) => (
          <LaneRows key={lane.key} lane={lane} now={now} pct={pct} />
        ))}
        {scenario.probes.length > 0 && <HealthStrip scenario={scenario} now={now} firstDeploy={scenarioKey === "first-deploy"} />}
        {scenario.run && <RunStrip scenario={scenario} now={now} pct={pct} />}
      </div>
    </div>
  );
}

function LaneRows({ lane, now, pct }: { lane: Lane; now: number; pct: (t: number) => string }) {
  return (
    <div className="flex items-start gap-2">
      <span className={`${LABEL_W} flex-none truncate pt-0.5 font-mono text-[11px] text-text-secondary`} title={lane.label}>
        {lane.label}
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        {lane.rows.map((row, i) => (
          <div key={i} className="relative h-5 rounded-sm bg-bg-primary/60">
            {row.map((bar) => (
              <BarView key={bar.process.name} bar={bar} now={now} pct={pct} />
            ))}
            <span className="absolute inset-y-0 w-px bg-text-secondary/50" style={{ left: pct(now) }} />
          </div>
        ))}
      </div>
    </div>
  );
}

// One pod or container, drawn up to now: each state it was in, in that state's colour.
function BarView({ bar, now, pct }: { bar: Bar; now: number; pct: (t: number) => string }) {
  if (bar.from > now) return null;
  const events = bar.process.events.filter((e) => e.t <= now);
  const end = Math.min(bar.to ?? now, now);
  const state = events[events.length - 1]?.state;
  return (
    <div
      className="absolute inset-y-0.5 overflow-hidden rounded-sm"
      style={{ left: pct(bar.from), width: `calc(${pct(end)} - ${pct(bar.from)} + 2px)` }}
      title={`${bar.process.name}${bar.version ? ` (${bar.version})` : ""}: ${state ? STATE_TEXT[state] : ""}${
        state === "failed" && bar.process.exit != null ? `, exit code ${bar.process.exit}` : ""
      }`}
    >
      {events.map((e, i) => {
        const to = i + 1 < events.length ? events[i + 1].t : end;
        if (e.state === "gone") return null;
        return (
          <span
            key={i}
            className="absolute inset-y-0"
            style={{
              left: `${((e.t - bar.from) / Math.max(1, end - bar.from)) * 100}%`,
              width: `${((to - e.t) / Math.max(1, end - bar.from)) * 100}%`,
              background: alpha(STATE_COLOR[e.state], e.state === "stopping" ? 60 : 70),
            }}
          />
        );
      })}
      {bar.version && <span className="relative px-1 text-[9.5px] font-medium leading-4 text-bg-primary">{bar.version}</span>}
    </div>
  );
}

// One tick per recorded probe, green when the app answered, red when it did not, grey before it ever had.
function HealthStrip({ scenario, now, firstDeploy }: { scenario: DeployScenario; now: number; firstDeploy: boolean }) {
  const h = healthAt(scenario, now, firstDeploy);
  return (
    <div className="mt-1 flex items-start gap-2 border-t border-border pt-2">
      <span className={`${LABEL_W} flex-none pt-0.5 text-[11px] text-text-secondary`}>health probe</span>
      <div className="min-w-0 flex-1">
        <svg className="block h-5 w-full rounded-sm bg-bg-primary/60" viewBox="0 0 1000 20" preserveAspectRatio="none" aria-hidden="true">
          {scenario.probes.map(([t, ok], i) => {
            if (t > now) return null;
            const before = firstDeploy && !ok && (h.firstAnswer == null || t < h.firstAnswer);
            return (
              <rect
                key={i}
                x={(t / scenario.duration) * 1000}
                y={ok ? 3 : 0}
                width={1.4}
                height={ok ? 14 : 20}
                fill={ok ? HUE.green : before ? NEUTRAL.lineStrong : HUE.redSoft}
              />
            );
          })}
        </svg>
        <p className="mt-1 font-mono text-[11px] text-text-muted">
          <span style={{ color: HUE.green }}>{h.answered} answered</span>
          {", "}
          <span style={h.failed ? { color: HUE.redSoft } : undefined}>{h.failed} failed</span>
          {h.before > 0 && `, ${h.before} before it was up`}
          {h.longestGap > 0 && `, longest without an answer ${(h.longestGap / 1000).toFixed(1)}s`}
          {firstDeploy && h.firstAnswer != null && `, first answer at ${clock(h.firstAnswer)}`}
        </p>
        <span className="sr-only">{`${h.answered} probes answered, ${h.failed} failed`}</span>
      </div>
    </div>
  );
}

// The run the release has to wait for: its state, and whether a drain holds new work back.
function RunStrip({ scenario, now, pct }: { scenario: DeployScenario; now: number; pct: (t: number) => string }) {
  const run = scenario.run;
  if (!run) return null;
  const at = runAt(scenario, now);
  const states = run.states.filter((s) => s.t <= now);
  const name = run.name.split(".").pop() ?? run.name;
  return (
    <div className="flex items-start gap-2">
      <span className={`${LABEL_W} flex-none pt-0.5 text-[11px] text-text-secondary`}>the busy run</span>
      <div className="min-w-0 flex-1">
        <div className="relative h-5 rounded-sm bg-bg-primary/60">
          {states.map((s, i) => {
            const to = i + 1 < states.length ? states[i + 1].t : now;
            return (
              <span key={i} className="absolute inset-y-0.5" style={{ left: pct(s.t), width: `calc(${pct(to)} - ${pct(s.t)})`, background: alpha(s.state === "in_progress" ? HUE.amber : HUE.green, 60) }}>
                {s.drain && <span className="absolute inset-x-0 bottom-0 h-1" style={{ background: HUE.violet }} />}
              </span>
            );
          })}
        </div>
        <p className="mt-1 text-[11px] leading-snug text-text-muted">
          Run {run.id} ({name}){at && <>: <span className="font-mono">{at.state}</span></>}
          {at?.drain && <span style={{ color: HUE.violet }}>, drain on</span>}. The recorder put a finished run back to in progress to stand
          for a long job, and ended it by hand at {clock(run.ended)}.
        </p>
      </div>
    </div>
  );
}

function useStickToEnd(count: number) {
  const box = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [count]);
  return box;
}

// The CLI's lines as they printed, and the commands it told the person to run, with the time each arrived.
function CliPanel({ scenario, now }: { scenario: DeployScenario; now: number }) {
  const shown = scenario.cli.filter((l) => l.t <= now);
  const box = useStickToEnd(shown.length);
  return (
    <div className="flex min-w-0 flex-col rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>Terminal</PanelTitle>
      <div ref={box} className="h-[200px] overflow-y-auto px-4 py-2 font-mono text-[11px] leading-relaxed">
        {shown.map((l, i) => (
          <div key={i} className="flex gap-3">
            <span className="w-9 flex-none text-right text-text-muted/70">{clock(l.t)}</span>
            <span className={`min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere] ${l.from === "you" ? "text-accent-bright" : l.from === "shell" ? "text-text-muted" : "text-text-secondary"}`}>
              {l.from === "you" ? `$ ${l.text}` : l.text}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

// Who tried to reach what, and what happened, each as it was checked.
function Checks({ scenario, now }: { scenario: DeployScenario; now: number }) {
  const checks = scenario.checks.filter((c) => c.t <= now);
  const egress = scenario.egress.filter((e) => e.t <= now);
  const tone = (good: boolean) => (good ? HUE.green : HUE.redSoft);
  return (
    <div className="flex min-w-0 flex-col rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>Who can reach what</PanelTitle>
      <div className="h-[260px] overflow-y-auto px-4 py-2 text-[11.5px] leading-relaxed">
        {checks.length === 0 && egress.length === 0 && <p className="text-text-muted">The checks run once the deploy is done.</p>}
        {checks.map((c, i) => (
          <div key={`c${i}`} className="flex gap-3 border-b border-border/50 py-1">
            <span className="w-9 flex-none text-right font-mono text-text-muted/70">{clock(c.t)}</span>
            <span className="min-w-0 flex-1 text-text-secondary">
              {c.who} <span className="text-text-muted">to</span> <span className="font-mono">{c.what}</span>
            </span>
            <span className="flex-none font-mono" style={{ color: tone(c.result === c.want) }}>
              {c.result}
            </span>
          </div>
        ))}
        {egress.length > 0 && <p className="pt-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">From inside the held pod, as the app</p>}
        {egress.map((e, i) => (
          <div key={`e${i}`} className="flex gap-3 border-b border-border/50 py-1 font-mono">
            <span className="w-12 flex-none text-text-muted">{e.kind}</span>
            <span className="min-w-0 flex-1 truncate text-text-secondary" title={e.target}>
              {e.target}
            </span>
            <span className="flex-none" style={{ color: tone(e.ok) }}>
              {e.got}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

function LogList({ title, lines }: { title: string; lines: { t: number; text: string }[] }) {
  const box = useStickToEnd(lines.length);
  return (
    <div className="flex min-w-0 flex-col rounded-lg border border-border bg-bg-primary">
      <PanelTitle>{title}</PanelTitle>
      <div ref={box} className="h-[180px] overflow-y-auto px-3 py-2 font-mono text-[11px] leading-relaxed text-text-secondary">
        {lines.length === 0 && <span className="text-text-muted">Nothing yet.</span>}
        {lines.map((l, i) => (
          <div key={i} className="flex gap-3">
            <span className="w-9 flex-none text-right text-text-muted/70">{clock(l.t)}</span>
            <span className="min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere]">{l.text}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

// What the whole recording shows, in a sentence, with its numbers read off it.
function Summary({ scenario, scenarioKey, target }: { scenario: DeployScenario; scenarioKey: ScenarioKey; target: TargetKey }) {
  return <p className="max-w-4xl text-sm text-text-secondary">{summaryOf(scenario, scenarioKey, target)}</p>;
}
