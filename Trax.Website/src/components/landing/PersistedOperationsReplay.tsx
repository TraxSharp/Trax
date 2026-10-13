"use client";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import data from "@/data/persisted-operations-recordings.json";
import { durationOf, viewAt, type Actor, type Card, type Outcome, type Recording } from "@/lib/persisted-operations-replay";

// Recorded from the PersistedOperations sample's API by Trax.Samples' scripts/recordings/persisted-operations.mjs,
// which writes this file with its --copy-to option. Re-record when the sample's host, train or manifest change.
const recording = data as unknown as Recording;
const stories = Array.isArray(recording.stories) ? recording.stories : [];

const ACTOR: Record<Actor, string> = { app: "The app", stranger: "Someone else", operator: "The operator" };

const OUTCOME: Record<Outcome, { label: string; tone: string }> = {
  waiting: { label: "sent", tone: "border-info/40 text-info" },
  answered: { label: "answered", tone: "border-accent/40 text-accent-bright" },
  accepted: { label: "accepted", tone: "border-accent/40 text-accent-bright" },
  refused: { label: "refused", tone: "border-derail/40 text-derail" },
};

export default function PersistedOperationsReplay() {
  const [key, setKey] = useState(stories[0]?.key ?? "calls");
  const [playing, setPlaying] = useState(false);
  const [elapsed, setElapsed] = useState(-1);
  const base = useRef(0);

  const story = stories.find((s) => s.key === key);
  const duration = durationOf(story);
  const active = playing && elapsed < duration;
  const view = useMemo(() => viewAt(story, elapsed), [story, elapsed]);

  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => setElapsed(Math.min(performance.now() - base.current, duration)), 50);
    return () => clearInterval(timer);
  }, [active, duration]);

  const play = useCallback(() => {
    base.current = performance.now();
    setElapsed(0);
    setPlaying(true);
  }, []);

  const reset = () => {
    setPlaying(false);
    setElapsed(-1);
  };

  // Play the first story once, the first time the demo is on screen, unless the reader prefers less motion.
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
        play();
      },
      { threshold: 0.2 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [play]);

  return (
    <div ref={root} className="grid gap-4 lg:grid-cols-[230px_minmax(0,1fr)_minmax(0,300px)]">
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        <Group label="Scenario">
          <select
            value={key}
            disabled={active}
            onChange={(e) => {
              setKey(e.target.value);
              reset();
            }}
            className="rounded-md border border-border bg-bg-primary px-2 py-2 text-sm text-text-primary outline-none focus:border-accent disabled:opacity-50"
          >
            {stories.map((s) => (
              <option key={s.key} value={s.key}>
                {s.title}
              </option>
            ))}
          </select>
        </Group>
        <Group label="Actions">
          <button
            onClick={play}
            disabled={active}
            className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-default disabled:opacity-40"
          >
            Play
          </button>
          <button
            onClick={reset}
            disabled={elapsed < 0}
            className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-left text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35 disabled:hover:border-border"
          >
            Reset
          </button>
        </Group>
      </div>

      <RequestsPanel cards={view.cards} />
      <PhonePanel greeting={view.phone.greeting} calling={view.phone.calling} />
      <StoredPanel stored={view.stored} proposed={view.proposed} />
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

function PanelTitle({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 border-b border-border px-4 py-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">
      {children}
    </div>
  );
}

// One card per request: who sent it, what they sent, and what came back.
function RequestsPanel({ cards }: { cards: Card[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the panel itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [cards.length]);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary lg:h-[600px]">
      <PanelTitle>Requests</PanelTitle>
      <div ref={box} className="flex h-[340px] flex-col gap-2.5 overflow-y-auto p-3 lg:h-auto lg:min-h-0 lg:flex-1">
        {cards.map((card) => (
          <div key={card.key} className="rounded-md border border-border bg-bg-primary/60 p-2.5 text-xs">
            <div className="flex items-center gap-2">
              <span className="font-medium text-text-secondary">{ACTOR[card.actor]}</span>
              <span className={`ml-auto rounded-full border px-1.5 py-px text-[10px] ${OUTCOME[card.outcome].tone}`}>
                {OUTCOME[card.outcome].label}
              </span>
            </div>
            <p className="mt-1.5 font-mono text-[11px] leading-relaxed text-text-muted [overflow-wrap:anywhere]">{card.sent}</p>
            <div className="mt-2 flex items-baseline gap-2 border-t border-border pt-1.5">
              {card.outcome === "waiting" ? (
                <span className="animate-pulse text-info">Waiting for the server…</span>
              ) : (
                <>
                  <span className={card.outcome === "refused" ? "text-derail" : "font-medium text-accent-bright"}>
                    &larr; {card.answer}
                  </span>
                  {card.ms != null && <span className="ml-auto shrink-0 text-[10px] text-text-muted">{card.ms} ms</span>}
                </>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// The shipped app: it only ever sends greet_v1 and a name.
function PhonePanel({ greeting, calling }: { greeting: string | null; calling: boolean }) {
  return (
    <div className="flex min-w-0 justify-center">
      <div className="flex h-[360px] w-full max-w-[300px] flex-col overflow-hidden rounded-[2rem] lg:h-[600px] border border-border bg-bg-secondary px-3 pb-3 pt-2.5">
        <div className="mx-auto mb-3 h-1.5 w-16 rounded-full bg-border" />
        <div className="flex flex-1 flex-col rounded-[1.4rem] border border-border bg-bg-primary px-4 py-4">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">Your app</p>
          <p className="text-xs text-text-secondary">Version 1.0, already in the store</p>
          <div className="flex flex-1 items-center justify-center">
            <p
              className={`text-center text-2xl font-semibold transition-colors ${greeting ? "text-text-primary" : "text-text-muted"} ${calling ? "animate-pulse" : ""}`}
            >
              {greeting ?? "…"}
            </p>
          </div>
          <p className="rounded-md border border-border px-2 py-1.5 font-mono text-[10.5px] text-text-muted">
            sends greet_v1 {"{"} name: &quot;Alice&quot; {"}"}
          </p>
        </div>
      </div>
    </div>
  );
}

// What the server runs when the app sends greet_v1, and the edit the operator sent.
function StoredPanel({
  stored,
  proposed,
}: {
  stored: string | null;
  proposed: { document: string; outcome: Outcome; against: string | null } | null;
}) {
  return (
    <div className="min-w-0 rounded-lg border border-border bg-bg-secondary pb-3 lg:col-span-3">
      <PanelTitle>Stored query: greet_v1</PanelTitle>
      {/* Room for both documents, so the operator's edit does not push the page down. */}
      <div className="mx-3 mt-3 flex min-h-[190px] flex-col gap-2">
        <Document label="On the server" text={stored} />
        {proposed && <Document label="Sent by the operator" text={proposed.document} outcome={proposed.outcome} against={proposed.against} />}
      </div>
    </div>
  );
}

function Document({
  label,
  text,
  outcome,
  against,
}: {
  label: string;
  text: string | null;
  outcome?: Outcome;
  against?: string | null;
}) {
  // Words that differ from the stored document are lit, so the change reads at a glance.
  const was = new Set((against ?? "").split(/(\s+)/));
  const words = (text ?? "nothing stored").split(/(\s+)/);
  const tone = outcome === "refused" ? "text-derail" : outcome === "accepted" ? "text-accent-bright" : "text-signal";
  return (
    <div className="rounded-md border border-border bg-bg-primary/60 p-3">
      <div className="mb-1.5 flex items-center gap-2 text-[11px] text-text-muted">
        {label}
        {outcome && (
          <span className={`ml-auto rounded-full border px-1.5 py-px text-[10px] ${OUTCOME[outcome].tone}`}>{OUTCOME[outcome].label}</span>
        )}
      </div>
      <p className="font-mono text-[12px] leading-relaxed text-text-secondary [overflow-wrap:anywhere]">
        {words.map((w, i) =>
          against !== undefined && w.trim() && !was.has(w) ? (
            <span key={i} className={`font-semibold ${tone}`}>
              {w}
            </span>
          ) : (
            <span key={i}>{w}</span>
          ),
        )}
      </p>
    </div>
  );
}
