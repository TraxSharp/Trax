"use client";

import Link from "next/link";
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useFitCode } from "./useFitCode";
import data from "@/data/recovery-recordings.json";
import {
  advance,
  badgeOf,
  begin,
  currentStep,
  keyOf,
  labelOf,
  lineOf,
  phaseOf,
  requeue,
  takeFork,
  type Attempt,
  type Line,
  type Phase,
  type Playback,
  type Recording,
  type Scenario,
  type Step,
} from "@/lib/recovery-replay";

// Recorded from the Recovery sample by Trax.Samples' scripts/recordings, which writes this file with its
// --copy-to option. Re-record when the sample's trains or its page change.
const recordings = data.recordings as Recording[];
const sources = data.sources as Record<Scenario, { file: string; code: string }>;

const TOPICS = [
  { key: "papers", label: "What the papers say about cold-weather battery wear" },
  { key: "wiki", label: "History of the telegraph" },
];
const ORDERS = [
  { key: "A-1001", label: "A-1001: $89, arrived broken" },
  { key: "A-1002", label: "A-1002: $420, never arrived" },
  { key: "A-1003", label: "A-1003: $35.50, changed my mind, 2 earlier refunds" },
];
const PHASES: { phases: Phase[]; label: string }[] = [
  { phases: ["starting", "runs"], label: "Runs" },
  { phases: ["breaks"], label: "Breaks" },
  { phases: ["recovers", "requeue"], label: "Recovers" },
];

export default function RecoveryReplay() {
  const [scenario, setScenario] = useState<Scenario>("RESEARCH");
  const [topic, setTopic] = useState(TOPICS[0].key);
  const [order, setOrder] = useState(ORDERS[0].key);
  const [crash, setCrash] = useState(true);
  const [playback, setPlayback] = useState<Playback | null>(null);
  const [elapsed, setElapsed] = useState(0);
  // Playback time is performance.now() minus this.
  const base = useRef(0);

  const phase = phaseOf(playback);
  const active = playback != null && !playback.waiting && !playback.finished;
  const locked = active && phase !== "done";

  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => {
      const now = performance.now() - base.current;
      setElapsed(now);
      setPlayback((p) => (p ? advance(p, now) : p));
    }, 50);
    return () => clearInterval(timer);
  }, [active]);

  const run = useCallback(
    (s: Scenario = scenario, subject: string = s === "RESEARCH" ? topic : order, withCrash: boolean = crash) => {
      const recording = recordings.find((r) => r.key === keyOf(s, subject, withCrash));
      if (!recording) return;
      base.current = performance.now();
      setElapsed(0);
      setPlayback(begin(recording));
    },
    [scenario, topic, order, crash],
  );

  const changeData = () => {
    if (!playback) return;
    const taken = takeFork(playback, recordings, "changeData");
    if (!taken) return;
    base.current -= taken.shift;
    setPlayback(taken.playback);
  };

  const askAfresh = () => {
    if (!playback) return;
    if (phase === "breaks") {
      const taken = takeFork(playback, recordings, "askAfresh");
      if (!taken) return;
      base.current -= taken.shift;
      setPlayback(taken.playback);
      return;
    }
    const asked = requeue(playback);
    if (!asked) return;
    base.current = performance.now() - asked.at;
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
        run("RESEARCH", TOPICS[0].key, true);
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

  const shown = playback?.recording.scenario ?? scenario;
  const step = playback ? currentStep(playback) : null;
  const reached = PHASES.findIndex((p) => p.phases.includes(phase));

  return (
    <div ref={root} className="grid gap-4 lg:grid-cols-[230px_minmax(0,1fr)_minmax(0,300px)]">
      {/* Controls */}
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        <Group label="Scenario">
          <div role="tablist" className="grid grid-cols-2 gap-0.5 rounded-md border border-border bg-bg-primary p-0.5">
            {(["RESEARCH", "REFUND"] as const).map((s) => (
              <button
                key={s}
                role="tab"
                aria-selected={scenario === s}
                disabled={locked}
                onClick={() => choose(() => setScenario(s))}
                className={`rounded px-2 py-1.5 text-xs font-medium transition-colors disabled:cursor-default ${
                  scenario === s ? "bg-bg-tertiary text-text-primary" : "text-text-muted hover:text-text-secondary"
                }`}
              >
                {s === "RESEARCH" ? "Research" : "Refund"}
              </button>
            ))}
          </div>
          <label className="flex flex-col gap-1.5 text-xs text-text-muted">
            {scenario === "RESEARCH" ? "Topic" : "Order"}
            <select
              value={scenario === "RESEARCH" ? topic : order}
              disabled={locked}
              onChange={(e) => choose(() => (scenario === "RESEARCH" ? setTopic : setOrder)(e.target.value))}
              className="rounded-md border border-border bg-bg-primary px-2 py-2 text-sm text-text-primary outline-none focus:border-accent disabled:opacity-50"
            >
              {(scenario === "RESEARCH" ? TOPICS : ORDERS).map((o) => (
                <option key={o.key} value={o.key}>
                  {o.label}
                </option>
              ))}
            </select>
          </label>
          <label className="flex cursor-pointer items-center gap-2.5 text-xs text-text-secondary">
            <input
              type="checkbox"
              checked={crash}
              disabled={locked}
              onChange={(e) => choose(() => setCrash(e.target.checked))}
              className="h-4 w-4 accent-derail"
            />
            Crash once ({scenario === "RESEARCH" ? "while it writes the report" : "in the step after the approval"})
          </label>
        </Group>

        <Group label="Actions">
          <button
            onClick={() => run()}
            disabled={locked}
            className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-default disabled:opacity-40"
          >
            Run
          </button>
          <ActionButton onClick={changeData} disabled={phase !== "breaks" || playback?.forkTaken !== "none"}>
            Change the data during the backoff
          </ActionButton>
          <div className="grid grid-cols-2 gap-2">
            <ActionButton
              onClick={askAfresh}
              disabled={
                !(phase === "breaks" && playback?.forkTaken === "none") && !(playback?.waiting && !playback.requeued)
              }
            >
              Ask afresh
            </ActionButton>
            <ActionButton onClick={() => setPlayback(null)} disabled={!playback || locked}>
              Reset
            </ActionButton>
          </div>
          <p className="min-h-[3.5em] text-xs leading-relaxed text-text-muted">
            {retryIn != null && playback?.forkTaken === "none" ? (
              <span className="text-signal">
                In the backoff: the retry starts in {retryIn.toFixed(1)} s. Change the data or ask afresh before it does.
              </span>
            ) : playback?.waiting && !playback.requeued ? (
              "The run is over. Ask afresh to run it again with every question put to the model."
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

      <CodePanel source={sources[shown]} step={playback?.recording.scenario === shown ? step : null} />
      <ConsolePanel lines={playback?.lines ?? []} />
      <TimelinePanel playback={playback} elapsed={elapsed} />

      <p className="text-xs leading-relaxed text-text-muted lg:col-span-3">
        Recorded from the{" "}
        <Link href="/docs/samples/recovery" className="text-accent hover:text-accent-hover">
          Recovery sample
        </Link>{" "}
        running against Postgres, and played back in your browser: every junction, answer and track arrived as a{" "}
        <Link href="/docs/effect/junction-events" className="text-accent hover:text-accent-hover">
          junction event
        </Link>
        . The model is the sample&apos;s stand-in, which answers like a typed decision model; one setting points it at{" "}
        <Link href="/docs/sdk-reference/configuration/add-nimble-decider" className="text-accent hover:text-accent-hover">
          Nimble
        </Link>{" "}
        instead.
      </p>
    </div>
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
  const lines = useMemo(() => source.code.split("\n"), [source.code]);
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
  useFitCode(pane, source.file);
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
      <pre ref={pane} className="flex-1 overflow-x-auto py-3 font-mono text-[12px] leading-[1.7] text-text-secondary">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          return (
            <div
              key={i}
              className={`flex w-max min-w-full border-l-2 pr-4 transition-colors duration-200 ${i === highlighted ? tone : "border-transparent"}`}
            >
              <span className="mr-4 w-8 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
              <span className="whitespace-pre">{highlight(line.slice(indent))}</span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}

const LINE_TONES: Record<Line["tone"], string> = {
  info: "text-text-secondary",
  model: "text-info",
  replay: "font-semibold text-signal",
  error: "font-semibold text-derail",
  success: "font-semibold text-accent-bright",
  system: "text-text-muted",
};

function ConsolePanel({ lines }: { lines: Line[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the console itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [lines.length]);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>Console</PanelTitle>
      <div ref={box} className="h-[340px] overflow-y-auto px-4 py-3 font-mono text-[11.5px] leading-relaxed lg:h-auto lg:flex-1 lg:basis-0">
        {lines.length === 0 && <p className="font-sans text-sm text-text-muted">Pick a scenario and press Run.</p>}
        {lines.map((l) => (
          <div key={l.key} className={`whitespace-pre-wrap py-px ${LINE_TONES[l.tone]}`}>
            <span className="mr-2 text-text-muted/70">{(l.t / 1000).toFixed(1).padStart(4, " ")}s</span>
            {l.text}
          </div>
        ))}
      </div>
    </div>
  );
}

const BADGE_TONES: Record<string, string> = {
  replay: "text-signal border-signal/40 bg-signal/10",
  afresh: "text-afresh border-afresh/40 bg-afresh/10",
  model: "text-accent-bright border-accent/40 bg-accent/10",
  failed: "text-derail border-derail/40 bg-derail/10",
};

function barTone(step: Step): string {
  if (step.kind !== "JUNCTION") return step.replayed ? "bg-signal text-bg-primary" : "bg-accent-muted text-accent-bright ring-1 ring-accent/60";
  if (step.state === "FAILED") return "bg-derail text-white";
  if (step.state === "IN_PROGRESS") return "bg-info text-bg-primary animate-pulse";
  return "bg-accent text-white";
}

function TimelinePanel({ playback, elapsed }: { playback: Playback | null; elapsed: number }) {
  const attempts = playback?.attempts ?? [];
  if (!playback || attempts.length === 0)
    return (
      <div className="rounded-lg border border-border bg-bg-secondary lg:col-span-3">
        <PanelTitle>Timeline</PanelTitle>
        <p className="px-4 py-4 text-sm text-text-muted">
          Each attempt&apos;s junctions, questions and tracks appear here as they happen.
        </p>
      </div>
    );

  const running = attempts.some((a) => a.trainState === "IN_PROGRESS" || a.trainState === "PENDING");
  const t0 = Math.min(...attempts.map((a) => a.start ?? 0));
  const ends = attempts.flatMap((a) => [a.end ?? 0, ...Object.values(a.steps).map((s) => s.end ?? s.start ?? 0)]);
  const t1 = Math.max(running ? elapsed : 0, ...ends, t0 + 1000);

  return (
    <div className="rounded-lg border border-border bg-bg-secondary pb-3 lg:col-span-3">
      <PanelTitle>Timeline</PanelTitle>
      {attempts.map((attempt) => (
        <Lane key={attempt.id} playback={playback} attempt={attempt} elapsed={elapsed} t0={t0} t1={t1} />
      ))}
    </div>
  );
}

function Lane({
  playback,
  attempt,
  elapsed,
  t0,
  t1,
}: {
  playback: Playback;
  attempt: Attempt;
  elapsed: number;
  t0: number;
  t1: number;
}) {
  const steps = Object.values(attempt.steps).sort((a, b) => a.position - b.position);
  const manifestIndex = playback.attempts.filter((a) => a.origin === "manifest").findIndex((a) => a.id === attempt.id);
  const maxRetries = playback.run?.maxRetries ?? 2;
  const [status, statusTone] =
    attempt.trainState === "COMPLETED"
      ? [manifestIndex > 0 ? "Recovered" : "Completed", "text-accent-bright border-accent/40"]
      : attempt.trainState === "FAILED"
        ? [
            attempt.origin === "manifest" && manifestIndex < maxRetries ? `Failed, retrying ${manifestIndex + 1}/${maxRetries}` : "Failed",
            "text-derail border-derail/40",
          ]
        : ["Running", "text-info border-info/40"];
  // A question's bar runs from the previous step's end to its answer: the time the model took.
  const bars: { step: Step; from: number; to: number }[] = [];
  let previousEnd = attempt.start ?? 0;
  for (const step of steps) {
    const started = step.start ?? previousEnd;
    const ended = step.end ?? (step.state === "IN_PROGRESS" ? elapsed : started);
    const from = step.kind === "JUNCTION" ? started : Math.min(previousEnd, started);
    const to = step.kind === "JUNCTION" ? ended : started;
    previousEnd = Math.max(previousEnd, to);
    bars.push({ step, from, to });
  }

  // The lane is a row: each bar and each gap between bars grows with the time it covers, so the lanes
  // keep one time axis, but a bar never shrinks below its name. A lane too long for the panel scrolls.
  const segments: ({ kind: "gap"; ms: number } | { kind: "bar"; ms: number; step: Step })[] = [];
  let cursor = t0;
  for (const { step, from, to } of bars) {
    if (from > cursor) segments.push({ kind: "gap", ms: from - cursor });
    segments.push({ kind: "bar", ms: Math.max(to - from, 0), step });
    cursor = Math.max(cursor, to);
  }
  if (t1 > cursor) segments.push({ kind: "gap", ms: t1 - cursor });

  return (
    <div className="mx-3 mt-3 rounded-md border border-border bg-bg-primary/60 p-3">
      <div className="mb-2 flex flex-wrap items-center gap-x-2.5 gap-y-1.5 text-xs text-text-muted">
        <span>
          <strong className="font-semibold text-text-primary">{labelOf(playback, attempt)}</strong> execution {attempt.id}
        </span>
        {steps.map((step) => {
          const badge = badgeOf(playback, attempt, step);
          return badge ? (
            <span key={step.position} className={`rounded-full border px-2 py-0.5 ${BADGE_TONES[badge.tone]}`}>
              {step.questionKey} = {step.answer}: {badge.text}
            </span>
          ) : null;
        })}
        {attempt.failureJunction && (
          <span className={`rounded-full border px-2 py-0.5 ${BADGE_TONES.failed}`}>crashed in {attempt.failureJunction}</span>
        )}
        <span className={`ml-auto rounded-full border px-2 py-0.5 ${statusTone}`}>{status}</span>
      </div>
      <div className="flex h-[34px] items-center overflow-x-auto rounded border border-border bg-bg-secondary px-px">
        {segments.map((segment, i) => {
          if (segment.kind === "gap") return <div key={`gap-${i}`} className="min-w-0" style={{ flex: `${segment.ms} 1 0px` }} />;
          const step = segment.step;
          const title = [
            step.kind === "ROUTE" ? `→ ${step.answer}` : step.kind === "JUNCTION" ? step.name : `${step.questionKey}?`,
            step.answer != null ? `answer ${step.answer}` : "",
            badgeOf(playback, attempt, step)?.text ?? "",
          ]
            .filter(Boolean)
            .join("\n");
          // A route is a marker between two steps and takes no width of its own.
          if (step.kind === "ROUTE")
            return <div key={step.position} title={title} className="z-10 -mx-[3px] h-3 w-[6px] shrink-0 rounded-full bg-border-hover" />;
          const label = step.kind === "JUNCTION" ? step.name : `${step.questionKey}?`;
          return (
            <div
              key={step.position}
              title={title}
              className={`flex h-6 min-w-max items-center whitespace-nowrap rounded px-1.5 text-[10.5px] font-medium ${barTone(step)}`}
              style={{ flex: `${Math.max(segment.ms, 1)} 1 0px` }}
            >
              {label}
            </div>
          );
        })}
      </div>
    </div>
  );
}
