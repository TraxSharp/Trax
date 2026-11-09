"use client";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import CodeFold from "./CodeFold";
import ReplayToolbar, { PrimaryButton, ToolButton, ToolbarPills } from "./ReplayToolbar";
import { alignChains, codeLineLayout, useFitCode } from "./useFitCode";
import data from "@/data/recovery-recordings.json";
import {
  advance,
  begin,
  currentStep,
  keyOf,
  lineOf,
  phaseOf,
  type Phase,
  type Playback,
  type Recording,
  type Scenario,
  type CrashAt,
  withCheckpointPauses,
  type Step,
  trackOf,
  modelCallsOf,
  type ModelCall,
  type LaneSlot,
  type TrackLane,
} from "@/lib/recovery-replay";
import { REPLAY_PACE } from "@/lib/replay-pace";

// Recorded from the Recovery sample by Trax.Samples' scripts/recordings, which writes this file with its
// --copy-to option. Re-record when the sample's trains or its page change.
const recordings = (data.recordings as Recording[]).map(withCheckpointPauses);
const sources = data.sources as Record<Scenario, { file: string; code: string }>;

// The recording holds three orders, four crash plans and the forks a reader can take during the backoff. The home
// page plays one run that shows both ways a retry recovers: the first order crashes while it pays, before the
// checkpoint, so the retry runs from the top with both answers replayed; that retry pays, stores the checkpoint and
// crashes while it emails, so the last retry starts from the checkpoint and only sends the email.
const ORDER = "A-1001";
const CRASH: CrashAt = "payingThenEmailing";
const PHASES: { phases: Phase[]; label: string }[] = [
  { phases: ["starting", "runs"], label: "Runs" },
  { phases: ["breaks"], label: "Breaks" },
  { phases: ["recovers", "requeue"], label: "Recovers" },
];

export default function RecoveryReplay() {
  const [playback, setPlayback] = useState<Playback | null>(null);
  const [elapsed, setElapsed] = useState(0);
  // The playback clock, in recording time, and the real time it was last moved at. It runs at the replays' slower
  // pace, except through a backoff, which waits in real time so its countdown reads true seconds.
  const clock = useRef(0);
  const lastTick = useRef(0);
  const restart = (at: number) => {
    clock.current = at;
    lastTick.current = performance.now();
  };

  const phase = phaseOf(playback);
  const phaseNow = useRef(phase);
  useEffect(() => {
    phaseNow.current = phase;
  }, [phase]);
  const active = playback != null && !playback.waiting && !playback.finished;
  const locked = active && phase !== "done";

  useEffect(() => {
    if (!active) return;
    lastTick.current = performance.now();
    const timer = setInterval(() => {
      const real = performance.now();
      clock.current += (real - lastTick.current) / (phaseNow.current === "breaks" ? 1 : REPLAY_PACE);
      lastTick.current = real;
      const now = clock.current;
      setElapsed(now);
      setPlayback((p) => (p ? advance(p, now) : p));
    }, 50);
    return () => clearInterval(timer);
  }, [active]);

  const run = useCallback(() => {
    const recording = recordings.find((r) => r.key === keyOf(ORDER, CRASH));
    if (!recording) return;
    restart(0);
    setElapsed(0);
    setPlayback(begin(recording));
  }, []);

  // Play the default run once, the first time the demo is on screen, unless the reader prefers less motion.
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
        run();
      },
      { threshold: 0.2 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [run]);

  const retryIn = useMemo(() => {
    if (!playback || phase !== "breaks") return null;
    const next = playback.recording.events.slice(playback.cursor).find((e) => e.type === "attempt");
    return next ? Math.max(0, (next.t - elapsed) / 1000) : null;
  }, [playback, phase, elapsed]);

  const step = playback ? currentStep(playback) : null;
  const reached = PHASES.findIndex((p) => p.phases.includes(phase));
  // The timeline is drawn in full before anything plays, for the run chosen.
  const chosen = recordings.find((r) => r.key === keyOf(ORDER, CRASH));
  const lanes = trackOf(playback?.recording ?? chosen, playback, elapsed);

  return (
    <div ref={root} className="flex flex-col gap-4">
      <ReplayToolbar
        caption={retryIn != null && playback?.forkTaken === "none" ? `Retrying in ${retryIn.toFixed(1)} s` : null}
        hint="Refund A-1001, $89, arrived broken. The payment is set to crash once, then the email once. Run it."
      >
        <PrimaryButton onClick={() => run()} disabled={locked}>
          Run
        </PrimaryButton>
        <ToolButton onClick={() => setPlayback(null)} disabled={!playback}>
          Reset
        </ToolButton>
        <ToolbarPills>
          {PHASES.map((p, i) => {
            // A run that never crashed skips Breaks and Recovers.
            const crashed = playback?.attempts.some((a) => a.trainState === "FAILED") ?? false;
            const done = phase === "done" ? i === 0 || crashed : i < reached;
            const tone = done
              ? "border-accent/50 text-accent-bright"
              : i === reached
                ? i === 1
                  ? "border-derail/60 text-derail"
                  : "border-info/60 text-info"
                : "border-border text-text-muted";
            return (
              <li key={p.label} className={`rounded-full border px-2.5 py-1 text-center ${tone}`}>
                {p.label}
              </li>
            );
          })}
        </ToolbarPills>
      </ReplayToolbar>

      <div className="grid gap-4 min-[1200px]:h-[360px] min-[1200px]:grid-cols-[minmax(0,1fr)_minmax(0,300px)]">
        <TimelinePanel lanes={lanes} />
        <ModelPanel calls={modelCallsOf(playback)} />
      </div>

      {/* The run is the demo; the train behind it is folded below it, lit in step with it. */}
      <CodeFold label="The refund workflow, lit as it runs" pulse={step ? `${playback?.attempts.length}:${step.position}:${step.state}` : null}
        color={!step ? undefined : step.replayed ? "#d4a24c" : step.state === "FAILED" ? "#c45c5c" : step.state === "COMPLETED" ? "#6cc596" : "#7a9ec2"}
      >
        <CodePanel source={sources.REFUND} step={step} />
      </CodeFold>

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

// The words that declare a decision and its tracks, picked out in the code.
const TOKEN =
  /(\.(?:Switch|Scale|Gate|When|AtLeast|Yes|No|Unsure)\b)|(\[Trax[A-Za-z]*)|("[^"]*")|(\b(?:public|class|protected|override)\b)/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1] ? "text-signal" : m[2] ? "text-info" : m[3] ? "text-accent-bright" : "text-text-muted";
    parts.push(
      <span key={m.index} className={tone}>
        {m[0]}
      </span>,
    );
    last = m.index + m[0].length;
  }
  parts.push(line.slice(last));
  return parts;
}

function CodePanel({ source, step }: { source: { file: string; code: string }; step: Step | null }) {
  const lines = useMemo(() => alignChains(source.code.split("\n")), [source.code]);
  // Only the chain, from Junctions() to Resolve(), so it stays on screen while the highlight moves. The line
  // numbers stay the file's.
  const [first, last, indent] = useMemo(() => {
    const at = lines.findIndex((l) => l.includes("Junctions()"));
    const end = lines.findIndex((l, i) => i > at && l.includes(".Resolve()"));
    const shown = at < 0 || end < 0 ? [0, lines.length] : [at, end + 1];
    const indent = Math.min(...lines.slice(shown[0], shown[1]).filter((l) => l.trim()).map((l) => l.search(/\S/)));
    return [shown[0], shown[1], indent];
  }, [lines]);
  const pane = useRef<HTMLPreElement>(null);
  useFitCode(pane, source.file, 12, 11);
  const highlighted = step ? lineOf(source.code, step) : -1;
  const tone =
    step?.state === "FAILED"
      ? "border-derail bg-derail/15"
      : step?.state === "IN_PROGRESS"
        ? "border-info bg-info/15"
        : "border-accent-bright bg-accent/15";

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>
        Code <span className="font-mono normal-case tracking-normal text-text-secondary">{source.file}</span>
      </PanelTitle>
      <pre ref={pane} className="overflow-x-hidden py-3 font-mono text-[12px] leading-[1.7] text-text-secondary">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          const layout = codeLineLayout(line.slice(indent));
          return (
            <div
              key={i}
              className={`flex ${layout.row} border-l-2 pr-4 transition-colors duration-200 ${i === highlighted ? tone : "border-transparent"}`}
            >
              <span className="mr-4 w-8 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
              <span className={layout.text} style={layout.style}>
                {highlight(layout.body)}
              </span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}

const BADGE_TONES: Record<string, string> = {
  replay: "border-signal/40 text-signal",
  afresh: "border-afresh/40 text-afresh",
  model: "border-accent/40 text-accent-bright",
};

// What the model was asked and what it answered, one card per question, newest at the bottom.
function ModelPanel({ calls }: { calls: ModelCall[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the panel itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [calls.length]);
  const model = calls.find((c) => c.model)?.model;

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>
        Model
        {model && <span className="ml-auto font-mono font-normal normal-case tracking-normal text-text-muted/80">{model}</span>}
      </PanelTitle>
      <div ref={box} className="flex h-[340px] flex-col gap-2.5 overflow-y-auto p-3 min-[1200px]:h-auto min-[1200px]:flex-1 min-[1200px]:basis-0">
        {calls.map((call) => (
          <div key={call.key} className="rounded-md border border-border bg-bg-primary/60 p-2.5 text-xs">
            <div className="flex items-center gap-2">
              <span className="font-mono text-[11px] text-text-secondary">{call.questionKey}?</span>
              <span className="text-[10px] text-text-muted">{call.attempt}</span>
              {call.badge ? (
                <span className={`ml-auto rounded-full border px-1.5 py-px text-[10px] ${BADGE_TONES[call.badge.tone] ?? "border-border text-text-muted"}`}>
                  {call.badge.text.replace(": model not asked", "")}
                </span>
              ) : null}
            </div>
            {call.question && (
              <div className="mt-1.5 text-text-secondary">
                <p className="text-text-primary">{call.question.instructions}</p>
                <ul className="mt-1 space-y-0.5 text-[11px] text-text-muted">
                  {(call.question.options ?? [
                    { name: "Yes", description: call.question.yes },
                    { name: "No", description: call.question.no },
                  ]).map((o) => (
                    <li key={o.name}>
                      <span className="font-mono text-text-secondary">{o.name}</span>
                      {o.description ? `: ${o.description}` : ""}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            <div className="mt-2 flex items-baseline gap-2 border-t border-border pt-1.5">
              {call.state === "waiting" ? (
                <span className="animate-pulse text-info">Waiting for the model…</span>
              ) : (
                <>
                  <span className="font-medium text-accent-bright">&rarr; {call.answer}</span>
                  {call.ms != null && <span className="ml-auto text-[10px] text-text-muted">{(call.ms / 1000).toFixed(1)} s</span>}
                </>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// The timeline is a fixed grid, as in the food order demo: one lane per attempt, one box per junction or question,
// each filling as the run reaches it.
const SLOT_TONES: Record<LaneSlot["tone"], { box: string; fill: string; text: string }> = {
  idle: { box: "border-border", fill: "", text: "text-text-muted" },
  saved: { box: "border-info/60", fill: "bg-info/20", text: "text-info" },
  restored: { box: "border-border-hover border-dashed", fill: "bg-text-muted/10", text: "text-text-secondary" },
  running: { box: "border-info/60 bg-info/5", fill: "bg-info/25", text: "text-info" },
  done: { box: "border-accent/55", fill: "bg-accent/25", text: "text-accent-bright" },
  failed: { box: "border-derail/60", fill: "bg-derail/25", text: "text-derail" },
  replayed: { box: "border-signal/60", fill: "bg-signal/20", text: "text-signal" },
  asked: { box: "border-afresh/60", fill: "bg-afresh/20", text: "text-afresh" },
};

const STATUS_TONES: Record<NonNullable<TrackLane["status"]>["tone"], string> = {
  idle: "border-border text-text-muted",
  running: "border-info/40 text-info",
  done: "border-accent/40 text-accent-bright",
  failed: "border-derail/40 text-derail",
};

// One row per attempt, time running left to right, every row the same steps so a restored step sits under the
// attempt that saved it. Too narrow for that, each attempt becomes a card and the cards sit side by side.
function TimelinePanel({ lanes }: { lanes: TrackLane[] }) {
  const columns = lanes[0]?.slots.length ?? 0;
  const grid = { gridTemplateColumns: `76px repeat(${columns}, minmax(0, 1fr))` };
  return (
    <div className="@container flex min-w-0 flex-col rounded-lg border border-border bg-bg-secondary pb-3">
      <PanelTitle>Timeline</PanelTitle>
      <div className="mx-3 mt-3 grid flex-1 content-start gap-3 [grid-template-columns:repeat(auto-fit,minmax(160px,1fr))] @3xl:hidden">
        {lanes.map((lane) => (
          <div key={lane.id} className="flex min-w-0 flex-col gap-1.5 rounded-md border border-border bg-bg-primary/60 p-3 text-[11px]">
            <div className="flex h-6 items-center justify-between gap-2">
              <span className="truncate font-medium text-text-secondary">{lane.label}</span>
              {lane.status && <StatusPill status={lane.status} />}
            </div>
            {lane.slots.map((slot) => (
              <SlotBox key={slot.key} slot={slot} />
            ))}
          </div>
        ))}
      </div>
      {/* Room for three attempts, the most a run shows, so a requeue does not push the page down. */}
      <div className="mx-3 mt-3 hidden flex-1 flex-col justify-center gap-2 rounded-md border border-border bg-bg-primary/60 p-4 text-[11px] @3xl:flex">
        {lanes.map((lane) => (
          <div key={lane.id} className="grid items-center gap-x-1.5" style={grid}>
            {/* The attempt's status under its name, so the steps get the width. */}
            <span className="flex min-w-0 flex-col items-start gap-1">
              <span className="truncate font-medium text-text-secondary">{lane.label}</span>
              {lane.status && <StatusPill status={lane.status} />}
            </span>
            {lane.slots.map((slot) => (
              <SlotBox key={slot.key} slot={slot} />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

function StatusPill({ status }: { status: NonNullable<TrackLane["status"]> }) {
  return <span className={`rounded-full border px-2 py-0.5 text-[10px] ${STATUS_TONES[status.tone]}`}>{status.text}</span>;
}

function SlotBox({ slot }: { slot: LaneSlot }) {
  const tone = SLOT_TONES[slot.tone];
  return (
    <div
      title={slot.title ?? slot.label}
      className={`relative h-11 overflow-hidden rounded border bg-bg-secondary transition-opacity ${tone.box} ${slot.dimmed ? "opacity-40" : ""}`}
    >
      <div className={`absolute inset-y-0 left-0 transition-[width] duration-100 ${tone.fill}`} style={{ width: `${slot.fill * 100}%` }} />
      {/* The note goes under the name, so a narrow box still shows the name whole. */}
      <span className={`relative flex h-full flex-col justify-center px-2 text-[11px] font-medium leading-tight ${tone.text}`}>
        <span className="truncate">{slot.label}</span>
        {slot.note && <span className="truncate text-[9.5px] font-normal opacity-80">{slot.note}</span>}
      </span>
    </div>
  );
}
