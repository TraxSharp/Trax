"use client";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { alignChains, codeLineLayout, useFitCode } from "./useFitCode";
import data from "@/data/recovery-recordings.json";
import {
  advance,
  begin,
  currentStep,
  keyOf,
  lineOf,
  phaseOf,
  requeue,
  takeFork,
  type Phase,
  type Playback,
  type Recording,
  type Scenario,
  type CrashAt,
  pastCheckpoint,
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

const ORDERS = [
  { key: "A-1001", label: "A-1001: $89, arrived broken" },
  { key: "A-1002", label: "A-1002: $420, never arrived" },
  { key: "A-1003", label: "A-1003: $35.50, changed my mind, 2 earlier refunds" },
];
const CRASHES: { key: CrashAt; label: string }[] = [
  { key: "emailing", label: "Crash once while emailing" },
  { key: "paying", label: "Crash once while paying" },
  { key: "none", label: "Don't crash" },
];
const PHASES: { phases: Phase[]; label: string }[] = [
  { phases: ["starting", "runs"], label: "Runs" },
  { phases: ["breaks"], label: "Breaks" },
  { phases: ["recovers", "requeue"], label: "Recovers" },
];

export default function RecoveryReplay() {
  const [order, setOrder] = useState(ORDERS[0].key);
  const [crash, setCrash] = useState<CrashAt>(CRASHES[0].key);
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

  const run = useCallback(
    (orderId: string = order, crashAt: CrashAt = crash) => {
      const recording = recordings.find((r) => r.key === keyOf(orderId, crashAt));
      if (!recording) return;
      restart(0);
      setElapsed(0);
      setPlayback(begin(recording));
    },
    [order, crash],
  );

  const changeData = () => {
    if (!playback) return;
    const taken = takeFork(playback, recordings, "changeData");
    if (!taken) return;
    clock.current += taken.shift;
    setPlayback(taken.playback);
  };

  const askAfresh = () => {
    if (!playback) return;
    if (phase === "breaks") {
      const taken = takeFork(playback, recordings, "askAfresh");
      if (!taken) return;
      clock.current += taken.shift;
      setPlayback(taken.playback);
      return;
    }
    const asked = requeue(playback);
    if (!asked) return;
    restart(asked.at);
    setElapsed(asked.at);
    setPlayback(asked.playback);
  };

  // Changing what to run clears the last run.
  const choose = (change: () => void) => {
    change();
    setPlayback(null);
  };

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
        run(ORDERS[0].key, CRASHES[0].key);
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
  const chosen = recordings.find((r) => r.key === keyOf(order, crash));
  // A crash after the checkpoint retries from it, with no question left to ask afresh or data to change.
  const forkable = phase === "breaks" && playback?.forkTaken === "none" && !pastCheckpoint(playback?.recording);
  const lanes = trackOf(playback?.recording ?? chosen, playback, elapsed);

  return (
    <div ref={root} className="grid gap-4 lg:grid-cols-[230px_minmax(0,1fr)_minmax(0,300px)]">
      {/* Controls */}
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        <Group label="Refund">
          <Picker label="Order" value={order} options={ORDERS} disabled={locked} onChange={(v) => choose(() => setOrder(v))} />
          <Picker
            label="Crash"
            value={crash}
            options={CRASHES}
            disabled={locked}
            onChange={(v) => choose(() => setCrash(v as CrashAt))}
          />
        </Group>

        <Group label="Actions">
          <button
            onClick={() => run()}
            disabled={locked}
            className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-default disabled:opacity-40"
          >
            Run
          </button>
          <ActionButton onClick={changeData} disabled={!forkable}>
            Change the data during the backoff
          </ActionButton>
          <div className="grid grid-cols-2 gap-2">
            <ActionButton
              onClick={askAfresh}
              disabled={!forkable && !(playback?.waiting && !playback.requeued)}
            >
              Ask afresh
            </ActionButton>
            <ActionButton onClick={() => setPlayback(null)} disabled={!playback}>
              Reset
            </ActionButton>
          </div>
          <p className="min-h-[3.5em] text-xs leading-relaxed text-text-muted">
            {retryIn != null && playback?.forkTaken === "none" ? (
              <span className="text-signal">
                Retrying in {retryIn.toFixed(1)} s
              </span>
            ) : null}
          </p>
        </Group>

        <Group label="Progress">
          <ol className="grid grid-cols-3 gap-1.5 text-xs font-medium">
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
                <li key={p.label} className={`rounded-full border px-2 py-1 text-center ${tone}`}>
                  {p.label}
                </li>
              );
            })}
          </ol>
        </Group>
      </div>

      <CodePanel source={sources.REFUND} step={step} />
      <ModelPanel calls={modelCallsOf(playback)} />
      <TimelinePanel lanes={lanes} />

    </div>
  );
}

function Picker({
  label,
  value,
  options,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  options: { key: string; label: string }[];
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label className="flex flex-col gap-1.5 text-xs text-text-muted">
      {label}
      <select
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className="rounded-md border border-border bg-bg-primary px-2 py-2 text-sm text-text-primary outline-none focus:border-accent disabled:opacity-50"
      >
        {options.map((o) => (
          <option key={o.key} value={o.key}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function Group({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-2.5">
      <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">{label}</span>
      {children}
    </div>
  );
}

function ActionButton({ children, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      {...props}
      className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-left text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35 disabled:hover:border-border"
    >
      {children}
    </button>
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
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary lg:h-[600px]">
      <PanelTitle>
        Code <span className="font-mono normal-case tracking-normal text-text-secondary">{source.file}</span>
      </PanelTitle>
      <pre ref={pane} className="min-h-0 flex-1 overflow-x-hidden overflow-y-auto py-3 font-mono text-[12px] leading-[1.7] text-text-secondary">
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
      <div ref={box} className="flex h-[340px] flex-col gap-2.5 overflow-y-auto p-3 lg:h-auto lg:flex-1 lg:basis-0">
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

function TimelinePanel({ lanes }: { lanes: TrackLane[] }) {
  const columns = lanes[0]?.slots.length ?? 0;
  const grid = { gridTemplateColumns: `96px repeat(${columns}, minmax(0, 1fr)) 84px` };
  return (
    <div className="min-w-0 rounded-lg border border-border bg-bg-secondary pb-3 lg:col-span-3">
      <PanelTitle>Timeline</PanelTitle>
      {/* Narrow screens: each attempt top to bottom. */}
      <div className="mx-3 mt-3 flex flex-col gap-3 md:hidden">
        {lanes.map((lane) => (
          <div key={lane.id} className="flex flex-col gap-1.5 rounded-md border border-border bg-bg-primary/60 p-3 text-[11px]">
            <div className="flex items-center justify-between">
              <span className="font-medium text-text-secondary">{lane.label}</span>
              {lane.status && <StatusPill status={lane.status} />}
            </div>
            {lane.slots.map((slot) => (
              <SlotBox key={slot.key} slot={slot} />
            ))}
          </div>
        ))}
      </div>
      <div className="mx-3 mt-3 hidden overflow-x-auto rounded-md border border-border bg-bg-primary/60 p-3 md:block">
        {/* Room for three attempts, the most a run shows, so a requeue does not push the page down. */}
        <div className="flex min-h-[84px] flex-col gap-1.5 text-[11px]" style={{ minWidth: 96 + 84 + columns * 110 }}>
          {lanes.map((lane) => (
            <div key={lane.id} className="grid items-center gap-x-1" style={grid}>
              <span className="truncate font-medium text-text-secondary">{lane.label}</span>
              {lane.slots.map((slot) => (
                <SlotBox key={slot.key} slot={slot} />
              ))}
              <span className="flex justify-end">{lane.status && <StatusPill status={lane.status} />}</span>
            </div>
          ))}
        </div>
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
      className={`relative h-6 overflow-hidden rounded border bg-bg-secondary transition-opacity ${tone.box} ${slot.dimmed ? "opacity-40" : ""}`}
    >
      <div className={`absolute inset-y-0 left-0 transition-[width] duration-100 ${tone.fill}`} style={{ width: `${slot.fill * 100}%` }} />
      <span className={`relative flex h-full items-center gap-1.5 truncate px-1.5 text-[10.5px] font-medium ${tone.text}`}>
        <span className="truncate">{slot.label}</span>
        {slot.note && <span className="ml-auto shrink-0 text-[9.5px] font-normal opacity-80">{slot.note}</span>}
      </span>
    </div>
  );
}
