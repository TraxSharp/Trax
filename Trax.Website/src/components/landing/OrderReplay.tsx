"use client";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { alignChains, codeLineLayout, useFitCode } from "./useFitCode";
import { replayNow } from "@/lib/replay-pace";
import data from "@/data/order-recordings.json";
import { ORDER, STAGES, durationOf, trackAt, viewAt, type OrderRecordings, type Scenario, type Slot, type Stage, type Tone, type Track } from "@/lib/order-replay";

// A food order taken by the Recovery sample's OrderMachine: what its laptop page and the phone received from the
// host, with the time each thing arrived. Re-record when the machine, its train or the page change.
const recordings = data as unknown as OrderRecordings;
const scenarios: Scenario[] = Array.isArray(recordings.scenarios) ? recordings.scenarios : [];

// A stage's colour at a given strength: a quiet tint for fills, a stronger one for borders.
const tint = (stage: Stage, alpha: number) =>
  `${STAGES[stage].color}${Math.round(alpha * 255)
    .toString(16)
    .padStart(2, "0")}`;

export default function OrderReplay() {
  const [key, setKey] = useState(scenarios[0]?.key ?? "order");
  const [playing, setPlaying] = useState<string | null>(null);
  const [elapsed, setElapsed] = useState(0);
  const base = useRef(0);

  const scenario = scenarios.find((s) => s.key === (playing ?? key));
  const duration = durationOf(scenario);
  const active = playing != null && elapsed < duration;
  const view = useMemo(() => (playing ? viewAt(recordings, scenario, elapsed) : null), [playing, scenario, elapsed]);
  // The timeline is drawn in full before anything plays, for the scenario chosen.
  const track = useMemo(() => trackAt(recordings, scenario, playing ? elapsed : -1), [scenario, playing, elapsed]);

  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => setElapsed(Math.min(replayNow() - base.current, duration)), 50);
    return () => clearInterval(timer);
  }, [active, duration]);

  // Counts runs, so the code panel starts each one following it again.
  const [runs, setRuns] = useState(0);
  const run = useCallback((k: string) => {
    setRuns((n) => n + 1);
    base.current = replayNow();
    setElapsed(0);
    setPlaying(k);
  }, []);

  const reset = () => {
    setPlaying(null);
    setElapsed(0);
  };

  // Play the first scenario once, the first time the demo is on screen, unless the reader prefers less motion.
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
        if (scenarios[0]) run(scenarios[0].key);
      },
      { threshold: 0.2 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [run]);

  const leaves = key === "left-in-cart";

  return (
    <div ref={root} className="grid gap-4 lg:grid-cols-[230px_minmax(0,1fr)_minmax(0,300px)]">
      {/* Laptop */}
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        <Group label="Scenario">
          <select
            value={key}
            disabled={active}
            onChange={(e) => {
              setKey(e.target.value as Scenario["key"]);
              reset();
            }}
            className="rounded-md border border-border bg-bg-primary px-2 py-2 text-sm text-text-primary outline-none focus:border-accent disabled:opacity-50"
          >
            {scenarios.map((s) => (
              <option key={s.key} value={s.key}>
                {s.title}
              </option>
            ))}
          </select>
        </Group>

        <Group label="Laptop">
          <div className="flex items-baseline justify-between rounded-md border border-border bg-bg-primary px-3 py-2">
            <span className="text-text-primary">{ORDER.item}</span>
            <span className="text-text-muted">{ORDER.price}</span>
          </div>
          <button
            onClick={() => run(key)}
            disabled={active}
            className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-default disabled:opacity-40"
          >
            {leaves ? "Leave it in the cart" : "Place order"}
          </button>
          <ActionButton onClick={reset} disabled={!playing}>
            Reset
          </ActionButton>
          <p className="min-h-[3.5em] text-xs leading-relaxed text-signal">{view?.note}</p>
        </Group>

        <Group label="Progress">
          <ol className="grid grid-cols-3 gap-1.5 text-xs font-medium">
            {(view?.progress ?? [
              { label: "Ordered", tone: "idle" as Tone, stage: "sent" as Stage },
              { label: "Cooking", tone: "idle" as Tone, stage: "cooking" as Stage },
              { label: "Delivered", tone: "idle" as Tone, stage: "delivered" as Stage },
            ]).map((p) => (
              <li
                key={p.label}
                className="rounded-full border border-border px-1 py-1 text-center text-text-muted transition-colors"
                style={
                  p.tone === "idle"
                    ? undefined
                    : { borderColor: tint(p.stage, 0.6), color: STAGES[p.stage].color, background: p.tone === "running" ? tint(p.stage, 0.12) : undefined }
                }
              >
                {p.label}
              </li>
            ))}
          </ol>
        </Group>
      </div>

      <CodePanel
        key={runs}
        machine={recordings.source}
        train={recordings.train?.source}
        lit={view?.lit ?? null}
        trainLit={view?.trainLit ?? null}
        revision={view?.revision ?? null}
      />
      <PhonePanel view={view} />
      <TimelinePanel track={track} train={recordings.train?.name ?? "DeliverOrderTrain"} />

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

// The words that declare a state, its events, its timer and the train it runs, and the train's steps, picked out.
const TOKEN =
  /(\.(?:In|On|Invokes|After|GoTo|To|OnDone|OnFailed|OnCancelled|Chain|Resolve)\b|\bChain(?=<))|(\[Trax[A-Za-z]*)|("[^"]*")|(\/\/.*$)|(\b(?:public|class|protected|override)\b)/g;

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

type Shown = "machine" | "train";

/**
 * The machine's code, or the train's while the order's run is on its junctions: the panel follows the run, and a
 * file tab picked by hand holds until the next run.
 */
function CodePanel({
  machine,
  train,
  lit,
  trainLit,
  revision,
}: {
  machine: { file: string; code: string } | undefined;
  train: { file: string; code: string } | undefined;
  lit: { line: number; tone: Tone; stage: Stage } | null;
  trainLit: { step: string; tone: Tone; stage: Stage } | null;
  revision: number | null;
}) {
  const [picked, setPicked] = useState<Shown | null>(null);
  const shown: Shown = picked ?? (trainLit && train ? "train" : "machine");
  const source = shown === "train" ? train : machine;
  const code = source?.code ?? "";
  const file = source?.file ?? "OrderMachine.cs";
  const lines = useMemo(() => alignChains(code.split("\n")), [code]);
  // Only the part that acts, so it stays on screen while the light moves: the machine's states, from the first
  // m.In(...) to the end of Configure, or the train's chain, from Junctions() to Resolve(). The line numbers stay
  // the file's.
  const [first, last, indent] = useMemo(() => {
    let at: number;
    let end = -1;
    if (shown === "train") {
      at = lines.findIndex((l) => l.includes("Junctions()"));
      end = lines.findIndex((l, i) => i > at && l.includes(".Resolve()"));
    } else {
      at = lines.findIndex((l) => l.trim().startsWith("m.In("));
      lines.forEach((l, i) => {
        if (i > at && /^\s*\.\w+\(.*\);\s*$/.test(l)) end = i;
      });
    }
    const range = at < 0 || end < 0 ? [0, lines.length] : [at, end + 1];
    const indent = Math.min(...lines.slice(range[0], range[1]).filter((l) => l.trim()).map((l) => l.search(/\S/)));
    return [range[0], range[1], Number.isFinite(indent) ? indent : 0];
  }, [lines, shown]);
  const litLine =
    shown === "train"
      ? trainLit
        ? { line: lines.findIndex((l) => l.includes(`Chain<${trainLit.step}>`)) + 1, stage: trainLit.stage }
        : null
      : lit;
  const pane = useRef<HTMLPreElement>(null);
  useFitCode(pane, file, 12, 11);

  const tab = (which: Shown, name: string | undefined) =>
    name && (
      <button
        onClick={() => setPicked(which)}
        className={`-mb-[9px] border-b-2 pb-2 font-mono normal-case tracking-normal transition-colors ${
          shown === which ? "border-accent text-text-primary" : "border-transparent text-text-muted hover:text-text-secondary"
        }`}
      >
        {name.split("/").pop()}
      </button>
    );

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary lg:h-[600px]">
      <PanelTitle>
        Code
        {tab("machine", machine?.file)}
        {tab("train", train?.file)}
        {revision != null && (
          <span className="ml-auto font-mono font-normal normal-case tracking-normal text-text-muted/70" title="The order's revision on the server">
            update {revision}
          </span>
        )}
      </PanelTitle>
      <pre ref={pane} className="min-h-0 flex-1 overflow-x-hidden overflow-y-auto py-3 font-mono text-[12px] leading-[1.7] text-text-secondary">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          const on = litLine != null && i + 1 === litLine.line;
          const layout = codeLineLayout(line.slice(indent));
          return (
            <div
              key={i}
              className={`flex ${layout.row} border-l-2 border-transparent pr-4 transition-colors duration-200`}
              style={on ? { borderLeftColor: STAGES[litLine.stage].color, background: tint(litLine.stage, 0.16) } : undefined}
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

function PhonePanel({ view }: { view: ReturnType<typeof viewAt> | null }) {
  const lines = view?.phone.lines ?? [];
  const stage = view?.phone.stage ?? null;
  const machine = view?.phone.machine ?? [
    { label: "Cart", stage: "cart" as Stage, at: "ahead" as const },
    { label: "Ordered", stage: "sent" as Stage, at: "ahead" as const },
    { label: "Delivered", stage: "delivered" as Stage, at: "ahead" as const },
  ];
  const box = useRef<HTMLDivElement>(null);
  // Scroll the list itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [lines.length]);

  // A plain phone: a rounded frame with a thin bar for the notch, showing a delivery app's screen.
  return (
    <div className="flex min-w-0 justify-center">
      <div className="flex h-[460px] w-full max-w-[300px] flex-col overflow-hidden rounded-[2rem] border border-border bg-bg-secondary px-3 pb-3 pt-2.5 lg:h-auto">
        <div className="mx-auto mb-3 h-1.5 w-16 rounded-full bg-border" />
        <div className="flex flex-1 flex-col overflow-hidden rounded-[1.4rem] border border-border bg-bg-primary">
          <div className="border-b border-border px-4 pb-3 pt-4">
            <p className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">Your order</p>
            <p className="text-xs text-text-secondary">{ORDER.restaurant}</p>
            <p className="mt-3 text-xl font-semibold transition-colors" style={{ color: stage ? STAGES[stage].color : "var(--text-muted)" }}>
              {view?.phone.status ?? "Nothing yet"}
            </p>
            <p className="mt-0.5 min-h-[1.5em] text-xs leading-relaxed text-text-muted">
              {view?.phone.detail}
            </p>
            <ol className="mt-3 flex items-center gap-1.5 text-[10px] text-text-muted" aria-label="The order's states">
              {machine.map((m, i) => (
                <li key={m.label} className="flex items-center gap-1.5">
                  {i > 0 && <span className="h-px w-3 bg-border" />}
                  <span
                    className="h-2 w-2 rounded-full border border-border"
                    style={m.at === "ahead" ? undefined : { borderColor: STAGES[m.stage].color, background: m.at === "current" ? STAGES[m.stage].color : tint(m.stage, 0.3) }}
                  />
                  <span style={m.at === "current" ? { color: STAGES[m.stage].color } : undefined}>{m.label}</span>
                </li>
              ))}
            </ol>
          </div>
          <div ref={box} className="min-h-0 flex-1 overflow-y-auto px-4 py-2 text-xs leading-snug">
            {lines.map((l) => (
              <div key={l.key} className="flex items-start gap-2.5 py-1.5">
                <span className="mt-1 h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: STAGES[l.stage].color }} />
                <span className="flex-1 text-text-secondary">{l.text}</span>
                <span className="shrink-0 text-[10px] text-text-muted/80">{l.clock}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

// The timeline is a fixed grid: the row names, then Cart, the five junctions (under Ordered), then the outcomes.
const GRID = "grid grid-cols-[124px_repeat(8,minmax(0,1fr))] gap-x-1";

function TimelinePanel({ track, train }: { track: Track; train: string }) {
  const [cart, ...rest] = track.machine;
  const ordered = rest.find((s) => s.key === "Ordered");
  const outcomes = rest.filter((s) => s.key !== "Ordered");
  return (
    <div className="min-w-0 rounded-lg border border-border bg-bg-secondary pb-3 lg:col-span-3">
      <PanelTitle>Timeline</PanelTitle>
      <StackedTimeline track={track} train={train} />
      <div className="mx-3 mt-3 hidden min-h-[114px] overflow-x-auto rounded-md border border-border bg-bg-primary/60 p-3 md:block">
        <div className="min-w-[720px] text-[11px]">
          <div className={GRID}>
            <RowName>OrderMachine</RowName>
            <SlotBox slot={cart} />
            {ordered ? (
              <div className="col-span-5">
                <SlotBox slot={ordered} />
              </div>
            ) : (
              <div className="col-span-5 flex items-center">
                {track.timer && (
                  <div className={`relative flex w-full items-center justify-center border-t border-dashed ${track.timer.lit ? "border-text-secondary/70" : "border-border"}`}>
                    <span className={`absolute bg-bg-primary px-1.5 text-[10px] ${track.timer.lit ? "text-text-secondary" : "text-text-muted"}`}>{track.timer.text}</span>
                  </div>
                )}
              </div>
            )}
            {outcomes.map((s) => (
              <SlotBox key={s.key} slot={s} />
            ))}
          </div>
          {track.train && (
            <>
              <div className={`${GRID} h-10`}>
                <div className="col-start-3 relative">
                  {track.starts && (
                    <Connector lit={track.starts.lit} side="left" tone="done">
                      starts the train
                    </Connector>
                  )}
                </div>
                <div className="col-start-7 relative">
                  {track.outcome && (
                    <Connector lit={track.outcome.lit} side="right" tone={track.outcome.tone}>
                      {track.outcome.text}
                    </Connector>
                  )}
                </div>
              </div>
              <div className={GRID}>
                <RowName>{train}</RowName>
                <div />
                {track.train.map((s) => (
                  <SlotBox key={s.key} slot={s} />
                ))}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

// On a narrow screen the same timeline reads top to bottom: the train's junctions indented under Ordered, between
// the two connectors, then the outcomes.
function StackedTimeline({ track, train }: { track: Track; train: string }) {
  const [cart, ...rest] = track.machine;
  const ordered = rest.find((s) => s.key === "Ordered");
  const outcomes = rest.filter((s) => s.key !== "Ordered");
  return (
    <div className="mx-3 mt-3 flex flex-col gap-1.5 rounded-md border border-border bg-bg-primary/60 p-3 text-[11px] md:hidden">
      <span className="font-medium text-text-secondary">OrderMachine</span>
      <SlotBox slot={cart} />
      {track.timer && (
        <div className={`ml-3 border-l border-dashed py-2 pl-3 text-[10px] ${track.timer.lit ? "border-text-secondary/70 text-text-secondary" : "border-border text-text-muted"}`}>
          {track.timer.text}
        </div>
      )}
      {ordered && <SlotBox slot={ordered} />}
      {track.train && (
        <div className="ml-3 flex flex-col gap-1.5 border-l border-border pl-3">
          {track.starts && <StackedLink lit={track.starts.lit} tone="done">starts the train</StackedLink>}
          <span className="font-medium text-text-secondary">{train}</span>
          {track.train.map((s) => (
            <SlotBox key={s.key} slot={s} />
          ))}
          {track.outcome && (
            <StackedLink lit={track.outcome.lit} tone={track.outcome.tone}>
              {track.outcome.text}
            </StackedLink>
          )}
        </div>
      )}
      <div className="grid grid-cols-2 gap-1.5">
        {outcomes.map((s) => (
          <SlotBox key={s.key} slot={s} />
        ))}
      </div>
    </div>
  );
}

function StackedLink({ lit, tone, children }: { lit: boolean; tone: Tone; children: React.ReactNode }) {
  const text = !lit ? "text-text-muted/60" : tone === "failed" ? "text-derail" : "text-text-secondary";
  return <span className={`text-[10px] ${text}`}>{children}</span>;
}

function RowName({ children }: { children: React.ReactNode }) {
  return <span className="flex h-6 items-center truncate font-medium text-text-secondary">{children}</span>;
}

function SlotBox({ slot }: { slot: Slot }) {
  // Empty and muted until reached; then the stage's quiet tint fills it left to right. A failed try is the colour of
  // a cancelled order.
  const stage: Stage = slot.tone === "failed" ? "cancelled" : slot.stage;
  const on = slot.tone !== "idle";
  return (
    <div
      title={slot.title ?? slot.label}
      className={`relative h-6 overflow-hidden rounded border border-border bg-bg-secondary transition-opacity ${slot.dimmed ? "opacity-40" : ""}`}
      style={on ? { borderColor: tint(stage, 0.55), background: slot.tone === "waiting" ? tint(stage, 0.07) : undefined } : undefined}
    >
      <div
        className="absolute inset-y-0 left-0 transition-[width] duration-100"
        style={{ width: `${slot.fill * 100}%`, background: on ? tint(stage, 0.24) : undefined }}
      />
      <span
        className={`relative flex h-full items-center gap-1.5 truncate px-1.5 text-[10.5px] font-medium ${on ? "" : "text-text-muted"}`}
        style={on ? { color: STAGES[stage].color } : undefined}
      >
        <span className="truncate">{slot.label}</span>
        {slot.attempt && <span className="ml-auto shrink-0 text-[9.5px] font-normal opacity-80">attempt {slot.attempt}</span>}
      </span>
    </div>
  );
}

// A thin vertical line between the machine's row and the train's, with a small label beside it: muted until it happens.
function Connector({ lit, side, tone, children }: { lit: boolean; side: "left" | "right"; tone: Tone; children: React.ReactNode }) {
  const color = !lit ? "bg-border" : tone === "failed" ? "bg-derail/70" : "bg-text-secondary/70";
  const text = !lit ? "text-text-muted/60" : tone === "failed" ? "text-derail" : "text-text-secondary";
  return (
    <>
      <div className={`absolute inset-y-0 w-px ${color} ${side === "left" ? "left-3" : "right-3"}`} />
      <span className={`absolute top-1/2 -translate-y-1/2 whitespace-nowrap text-[10px] ${text} ${side === "left" ? "left-5" : "right-5"}`}>{children}</span>
    </>
  );
}
