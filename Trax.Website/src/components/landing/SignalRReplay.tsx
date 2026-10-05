"use client";

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useFitCode } from "./useFitCode";
import data from "@/data/signalr-recordings.json";
import {
  apply,
  initial,
  lineOf,
  ping,
  probe,
  signIn,
  signOut,
  type HubState,
  type Outcome,
  type RunCard,
  type Scheduled,
  type SignalRRecording,
  type SourceKey,
  type Traffic,
} from "@/lib/signalr-replay";

// Recorded from the SignalRBroadcaster sample by Trax.Samples' scripts/recordings/signalr-broadcaster.mjs, which
// writes this file with its --copy-to option. Re-record when the sample's server or page changes.
const rec = data as SignalRRecording;

const BUTTONS: { outcome: Outcome; title: string; text: string }[] = [
  { outcome: "Succeed", title: "A ping that succeeds", text: "Started, then Completed." },
  {
    outcome: "FailForClients",
    title: "A ping that fails, for readers",
    text: "It throws a TrainException, a message written for whoever watches, so the hub sends it.",
  },
  {
    outcome: "FailUnexpectedly",
    title: "A ping that fails, internally",
    text: "Any other exception may name a host or a credential, so the hub sends a fixed sentence.",
  },
];

const FILES: { key: SourceKey; label: string }[] = [
  { key: "program", label: "Program.cs" },
  { key: "projection", label: "LiveTrainEvent.cs" },
  { key: "junction", label: "PingJunction.cs" },
];

const clock = (ms: number) =>
  new Date(ms).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });

interface Pending {
  due: number;
  s: Scheduled;
  seq: number;
}

export default function SignalRReplay() {
  const [state, setState] = useState<HubState>(initial);
  // The file a reader picked, and the focus it was picked under: it holds until the player points somewhere new.
  const [picked, setPicked] = useState<{ source: SourceKey; under: HubState["focus"] }>({ source: "program", under: null });
  const queue = useRef<Pending[]>([]);
  const seq = useRef(0);
  const presses = useRef<Record<Outcome, number>>({ Succeed: 0, FailForClients: 0, FailUnexpectedly: 0 });
  const [busy, setBusy] = useState(false);

  // Each press schedules its recorded messages at their recorded offsets; this applies them as they fall due.
  useEffect(() => {
    const timer = setInterval(() => {
      const now = performance.now();
      const due = queue.current.filter((p) => p.due <= now).sort((a, b) => a.due - b.due || a.seq - b.seq);
      if (due.length === 0) return;
      queue.current = queue.current.filter((p) => p.due > now);
      const wall = Date.now();
      setState((s) => due.reduce((acc, p) => apply(acc, p.s, wall - (now - p.due), p.seq), s));
      setBusy(queue.current.some((p) => p.s.kind === "signedIn" || p.s.kind === "connected" || p.s.kind === "signedOut"));
    }, 30);
    return () => clearInterval(timer);
  }, []);

  const press = useCallback((scheduled: Scheduled[]) => {
    const now = performance.now();
    // Signing out reloads the page: whatever was still on its way to the old one never arrives.
    if (scheduled.some((s) => s.kind === "signedOut")) queue.current = [];
    for (const s of scheduled) queue.current.push({ due: now + s.at, s, seq: seq.current++ });
    setBusy(scheduled.some((s) => s.kind === "signedIn" || s.kind === "signedOut"));
  }, []);

  const run = useCallback((outcome: Outcome) => press(ping(rec, outcome, presses.current[outcome]++)), [press]);

  const source = picked.under === state.focus || !state.focus ? picked.source : state.focus.source;
  const pick = (s: SourceKey) => setPicked({ source: s, under: state.focus });

  return (
    <div className="grid gap-4 lg:grid-cols-[230px_minmax(0,1fr)_minmax(0,300px)]">
      {/* Controls: the sign-in gate, or who is watching and the buttons */}
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        {!state.signedIn ? (
          <Group label="Sign in">
            <p className="text-xs leading-relaxed text-text-secondary">
              The hub sends every train&apos;s events to every client it lets in, so it has to say who may connect.
              Here that is anyone signed in with the Operator role; the cookie travels with the connection by
              itself.
            </p>
            <button
              onClick={() => press(signIn(rec))}
              disabled={busy}
              className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-default disabled:opacity-40"
            >
              Sign in as the demo operator
            </button>
            <ActionButton onClick={() => press(probe(rec))} disabled={busy}>
              Try to connect without signing in
            </ActionButton>
            {state.probe && <p className="break-all font-mono text-[11px] leading-relaxed text-derail">{state.probe}</p>}
            <p className="text-xs text-text-muted">The demo sign-in exists only in Development.</p>
          </Group>
        ) : (
          <>
            <Group label="Who is watching">
              <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5 text-xs">
                <dt className="text-text-muted">Signed in as</dt>
                <dd className="text-text-primary">{state.who}</dd>
                <dt className="text-text-muted">Hub</dt>
                <dd className={state.hub === "connected" ? "text-accent-bright" : "text-info"}>{state.hub}</dd>
                <dt className="text-text-muted">Connection</dt>
                <dd className="truncate font-mono text-[11px] text-text-secondary">{state.connectionId ?? "…"}</dd>
              </dl>
            </Group>
            <Group label="Run a train">
              {BUTTONS.map((b) => (
                <button
                  key={b.outcome}
                  onClick={() => run(b.outcome)}
                  disabled={state.hub !== "connected"}
                  className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-left transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35"
                >
                  <span className="flex items-center gap-2 text-sm text-text-primary">
                    <span
                      className={`h-2 w-2 rounded-full ${b.outcome === "Succeed" ? "bg-accent-bright" : b.outcome === "FailForClients" ? "bg-signal" : "bg-derail"}`}
                    />
                    {b.title}
                  </span>
                  <span className="mt-1 block text-xs leading-snug text-text-muted">{b.text}</span>
                </button>
              ))}
              <p className="text-xs leading-relaxed text-text-muted">
                Each posts to <code className="text-text-secondary">/pings</code>, which answers{" "}
                <code className="text-text-secondary">202 Accepted</code> and nothing else. The run&apos;s events
                arrive over the hub.
              </p>
            </Group>
            <button
              onClick={() => press(signOut(rec))}
              disabled={busy}
              className="self-start text-xs text-text-muted underline-offset-2 hover:text-text-secondary hover:underline disabled:opacity-40"
            >
              Sign out
            </button>
          </>
        )}
      </div>

      <RunsPanel state={state} />
      <TrafficPanel traffic={state.traffic} />
      <CodePanel source={source} onSource={pick} state={state} />

      <p className="text-xs leading-relaxed text-text-muted lg:col-span-3">
        Recorded from the SignalRBroadcaster sample and played back in your browser: every request, response and
        TrainEvent above is one the sample&apos;s page sent or received, at the time it arrived. The external ids are
        the recorded runs&apos;.
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
      className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-left text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35"
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

const STATUS_TONES: Record<RunCard["status"], string> = {
  Running: "text-info border-info/40",
  Completed: "text-accent-bright border-accent/40",
  Failed: "text-derail border-derail/40",
};

const STEP_TONES: Record<string, string> = {
  Started: "bg-info/15 text-info",
  Completed: "bg-accent/20 text-accent-bright",
  Failed: "bg-derail/20 text-derail",
};

function RunsPanel({ state }: { state: HubState }) {
  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>
        Runs, live
        <span className="ml-auto font-normal normal-case tracking-normal">
          {state.runs.length} run{state.runs.length === 1 ? "" : "s"}
        </span>
      </PanelTitle>
      <div className="h-[360px] overflow-y-auto p-3">
        {!state.signedIn ? (
          <p className="px-1 py-1 text-sm text-text-muted">
            Sign in to connect to the hub. Every run of the ping train shows up here as it happens.
          </p>
        ) : state.runs.length === 0 ? (
          <p className="px-1 py-1 text-sm text-text-muted">No runs yet. Run a train.</p>
        ) : (
          <ul className="space-y-2.5">
            {state.runs.map((run) => (
              <li key={`${run.externalId}-${run.steps[0].at}`} className="rounded-md border border-border bg-bg-primary/60 p-3">
                <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                  <span className="text-sm font-medium text-text-primary">{run.trainName}</span>
                  <span className="truncate font-mono text-[11px] text-text-muted">{run.externalId}</span>
                  <span className={`ml-auto rounded-full border px-2 py-0.5 text-xs ${STATUS_TONES[run.status]}`}>{run.status}</span>
                </div>
                <div className="mt-2 flex flex-wrap items-center gap-1.5 text-xs">
                  {run.steps.map((step, i) => (
                    <span key={i} className="flex items-center gap-1.5">
                      {i > 0 && <span className="text-text-muted">→</span>}
                      {step.gapMs != null && <span className="font-mono text-[11px] text-text-muted">+{Math.max(0, step.gapMs)} ms</span>}
                      <span className={`rounded px-2 py-0.5 ${STEP_TONES[step.eventType]}`}>
                        {step.eventType} <span className="opacity-70">{clock(step.at)}</span>
                      </span>
                    </span>
                  ))}
                </div>
                {run.reason && (
                  <p className={`mt-2 text-xs ${run.reason.withheld ? "text-text-secondary" : "text-signal"}`}>
                    <span className="mr-2 font-semibold uppercase tracking-wider">
                      {run.reason.withheld ? "Reason withheld" : "Reason"}
                    </span>
                    {run.reason.text}
                  </p>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function TrafficPanel({ traffic }: { traffic: Traffic[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the panel itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [traffic.length]);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>Raw traffic</PanelTitle>
      <div ref={box} className="h-[360px] overflow-y-auto px-3 py-2">
        {traffic.length === 0 && (
          <p className="px-1 py-1 text-sm text-text-muted">What the page sends, and every message the hub pushes.</p>
        )}
        {traffic.map((t) => (
          <div key={t.key} className="border-b border-border/50 py-1.5 last:border-0">
            <div className="flex items-center gap-2 text-xs">
              <span className={t.direction === "out" ? "text-info" : t.direction === "in" ? "text-accent-bright" : "text-text-muted"}>
                {t.direction === "out" ? "↑" : t.direction === "in" ? "↓" : "•"}
              </span>
              <span
                className={`rounded px-1.5 py-px font-mono text-[10px] ${t.channel === "hub" ? "bg-signal/15 text-signal" : "bg-info/15 text-info"}`}
              >
                {t.channel === "hub" ? "HUB" : "HTTP"}
              </span>
              <span className="min-w-0 flex-1 truncate text-text-primary">{t.title}</span>
              <span className="font-mono text-[10px] text-text-muted">{clock(t.at)}</span>
            </div>
            {t.note && <p className="mt-0.5 pl-5 text-[11px] leading-snug text-text-muted">{t.note}</p>}
            {t.detail !== undefined && (
              <pre className="mt-1 ml-5 overflow-x-auto rounded bg-bg-primary p-2 font-mono text-[10.5px] leading-snug text-text-secondary">
                {JSON.stringify(t.detail, null, 2)}
              </pre>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

const TOKEN = /(\/\/.*$)|("[^"]*")|(\b(?:public|private|static|const|var|new|async|await|return|throw|record|sealed|class|using|namespace)\b)/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1] ? "text-text-muted/70" : m[2] ? "text-accent-bright" : "text-text-muted";
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

function CodePanel({ source, onSource, state }: { source: SourceKey; onSource(s: SourceKey): void; state: HubState }) {
  const { file, code } = rec.sources[source];
  const lines = code.split("\n");
  const highlighted = state.focus && state.focus.source === source ? lineOf(code, state.focus) : -1;
  const box = useRef<HTMLDivElement>(null);
  useFitCode(box, file);
  const target = useRef<HTMLDivElement>(null);

  // Bring the highlighted line to the middle of the panel, scrolling the panel only.
  useLayoutEffect(() => {
    const b = box.current;
    const t = target.current;
    if (!b || !t) return;
    b.scrollTop = Math.max(0, t.offsetTop - b.clientHeight / 2 + t.clientHeight / 2);
  }, [highlighted, source]);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary lg:col-span-3">
      <div className="flex flex-wrap items-center gap-1 border-b border-border px-2">
        <span className="px-2 py-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">Code</span>
        {FILES.map((f) => (
          <button
            key={f.key}
            onClick={() => onSource(f.key)}
            className={`-mb-px border-b-2 px-3 py-2 font-mono text-xs transition-colors ${
              f.key === source ? "border-accent text-text-primary" : "border-transparent text-text-muted hover:text-text-secondary"
            }`}
          >
            {f.label}
          </button>
        ))}
        <span className="ml-auto hidden px-2 font-mono text-[11px] text-text-muted sm:inline">{file}</span>
      </div>
      <div ref={box} className="relative h-[300px] overflow-auto py-3 font-mono text-[12px] leading-[1.7] text-text-secondary">
        {lines.map((line, i) => (
          <div
            key={i}
            ref={i === highlighted ? target : undefined}
            className={`flex w-max min-w-full border-l-2 pr-4 transition-colors duration-200 ${
              i === highlighted ? "border-accent-bright bg-accent/15" : "border-transparent"
            }`}
          >
            <span className="mr-4 w-8 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
            <span className="whitespace-pre">{highlight(line)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
