"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import CodeFold from "./CodeFold";
import ReplayToolbar, { PrimaryButton, ToolButton } from "./ReplayToolbar";
import {
  INTENT_TONE,
  clientColor,
  movesOf,
  viewAt,
  type BoardRecordings,
  type Intent,
  type PodView,
  type RuleView,
  type TicketView,
  type Tone,
} from "@/lib/board-replay";

// One of the Board sample's recorded scenarios, replayed a move at a time: the board the server holds, the client each
// agent works in, and what the server answered. It plays itself once the first time it is on screen; Back and Next
// take over.
const TONE: Record<Tone, string> = { ok: "#6cc596", bad: "#d97a7a", warn: "#d4a24c", info: "#7a9ec2", idle: "#8f8b84" };
const PACE_MS = 2600;
// How a marked move is named: on the ticket it touched, and in the rule panel.
const INTENT_TEXT: Record<Intent, { ticket: string; rule: string }> = {
  "not-allowed": { ticket: "refused", rule: "Refused by" },
  "bad-evidence": { ticket: "rejected", rule: "Rejected by" },
  finding: { ticket: "found", rule: "Found by" },
};
// The rule panel's fixed height, in pixels: room for the longest span (six lines) and the server's message.
const RULE_HEIGHT = 196;
// A card's and a lane's fixed heights, in pixels: each text block on a card has room for a set number of lines and is
// clamped to it, so a longer note can never run into the line below.
const CARD_HEIGHT = 160;
const LANE_CHROME = 70;

export default function BoardReplay({ recordings, scenarioKey }: { recordings: BoardRecordings; scenarioKey: string }) {
  const [move, setMove] = useState(0);
  const [playing, setPlaying] = useState(false);
  const scenario = recordings.scenarios.find((s) => s.key === scenarioKey);
  const last = movesOf(scenario);
  const view = useMemo(() => viewAt(recordings, scenario, move), [recordings, scenario, move]);
  // Every lane is as tall as the most tickets any lane holds anywhere in the scenario, so no lane ever grows.
  const laneHeight = useMemo(() => {
    let most = 1;
    for (let m = 0; m <= movesOf(scenario); m++) {
      const v = viewAt(recordings, scenario, m);
      for (const l of recordings.lanes) most = Math.max(most, v.tickets.filter((t) => t.lane === l.key).length);
    }
    return LANE_CHROME + most * (CARD_HEIGHT + 8);
  }, [recordings, scenario]);

  // A replay that reaches the last move simply has nothing left to play.
  const running = playing && move < last;
  useEffect(() => {
    if (!running) return;
    const timer = setTimeout(() => setMove((m) => m + 1), move === 0 ? 900 : PACE_MS);
    return () => clearTimeout(timer);
  }, [running, move]);

  // Play the first scenario once, the first time the demo is on screen and the tab is in front, unless the reader
  // prefers less motion.
  const root = useRef<HTMLDivElement>(null);
  const autoplayed = useRef(false);
  useEffect(() => {
    const node = root.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    let seen = false;
    const start = () => {
      if (!seen || autoplayed.current || document.visibilityState !== "visible") return;
      autoplayed.current = true;
      setPlaying(true);
    };
    const observer = new IntersectionObserver(
      ([entry]) => {
        seen = entry.intersectionRatio >= 0.35;
        start();
      },
      { threshold: [0, 0.35] },
    );
    observer.observe(node);
    document.addEventListener("visibilitychange", start);
    return () => {
      observer.disconnect();
      document.removeEventListener("visibilitychange", start);
    };
  }, []);

  const step = (to: number) => {
    autoplayed.current = true;
    setPlaying(false);
    setMove(Math.max(0, Math.min(last, to)));
  };

  return (
    <div ref={root} className="flex flex-col gap-4">
      {/* The caption is as tall as the longest the recording gives (177 characters, two lines at full width), so the
          board below never moves as captions change length. */}
      <ReplayToolbar caption={move === 0 ? scenario?.caption : view.caption} reserve="min-h-[7.5em] md:min-h-[4.5em] lg:min-h-[3em]">
        <ToolButton onClick={() => step(move - 1)}>Back</ToolButton>
        <PrimaryButton onClick={() => step(move + 1)}>Next move</PrimaryButton>
        <ToolButton onClick={() => step(0)}>Start over</ToolButton>
        <span className="font-mono text-xs text-text-muted">
          move {move} / {last}
        </span>
      </ReplayToolbar>

      {/* The board across the whole width, then the clients in one row, then the code that refused a move, folded. */}
      <div className="flex flex-col gap-2 rounded-[10px] border border-[#2f3a2e] bg-[#131512] p-2.5">
        <div className="flex items-baseline gap-2.5 px-0.5">
          <span className="text-xs font-semibold uppercase tracking-wider text-accent-bright">Trax server</span>
          <span className="text-xs text-text-muted">the board: TicketMachine, which only the server moves</span>
        </div>
        <div className="grid gap-2 [grid-template-columns:repeat(auto-fit,minmax(150px,1fr))]">
          {recordings.lanes.map((lane) => {
            const cards = view.tickets.filter((t) => t.lane === lane.key);
            return (
              <div key={lane.key} style={{ height: laneHeight }} className="flex min-w-0 flex-col gap-2 overflow-hidden rounded-lg border border-border bg-bg-secondary p-2.5">
                <div className="flex items-baseline justify-between gap-1.5">
                  <span className="text-xs font-semibold uppercase tracking-wide text-text-secondary">{lane.name}</span>
                  <span className="font-mono text-[11px] text-text-muted">{cards.length}</span>
                </div>
                <div className="flex min-h-4 items-center gap-1.5 text-[11px] text-text-secondary">
                  <Dot color={clientColor(recordings, lane.worker)} />
                  <span className="truncate">{lane.worker ?? "no one"}</span>
                </div>
                {cards.map((t) => (
                  <TicketCard key={t.id} ticket={t} holderColor={clientColor(recordings, t.holder)} />
                ))}
              </div>
            );
          })}
        </div>
      </div>

      <div>
        <div className="grid min-w-0 content-start gap-2 [grid-template-columns:repeat(auto-fit,minmax(200px,1fr))]">
          {view.pods.map((pod) => (
            <Pod key={pod.name} pod={pod} color={clientColor(recordings, pod.name)} laneName={recordings.lanes.find((l) => l.key === pod.lane)?.name} />
          ))}
        </div>
      </div>

      <CodeFold label="The code that refused each move" pulse={view.rule ? move : null} color={view.rule ? TONE[INTENT_TONE[view.rule.intent]] : undefined}>
        <RulePanel rule={view.rule} />
      </CodeFold>
    </div>
  );
}

function Dot({ color }: { color: string }) {
  return <span className="inline-block size-2 flex-none rounded-full" style={{ background: color }} />;
}

function TicketCard({ ticket, holderColor }: { ticket: TicketView; holderColor: string }) {
  const chip = ticket.state === "Done" ? "#52ab7e" : ticket.live ? "#6cc596" : "#8f8b84";
  // A move a worker got wrong lights the card in its tone instead of the usual green.
  const wrong = ticket.attempt ? TONE[INTENT_TONE[ticket.attempt.intent]] : null;
  return (
    <div
      style={{
        height: CARD_HEIGHT,
        ...(wrong ? { borderColor: wrong, boxShadow: `0 0 0 3px ${wrong}33` } : {}),
      }}
      className={`flex flex-none flex-col gap-1.5 overflow-hidden rounded-md border bg-bg-primary px-2.5 py-2 transition-colors ${
        ticket.live ? "border-accent shadow-[0_0_0_3px_rgba(61,139,55,0.15)]" : "border-border"
      }`}
    >
      <div className="flex flex-none flex-nowrap items-center justify-between gap-1 font-mono text-[10.5px] text-text-muted">
        <span className="flex-none">{ticket.id}</span>
        <span className="min-w-0 truncate rounded px-1" title={ticket.state} style={{ border: `1px solid ${chip}55`, color: chip }}>
          {ticket.state}
        </span>
      </div>
      <div title={ticket.title} className="h-[53px] flex-none line-clamp-3 text-[12.5px] leading-[1.4] text-text-primary">
        {ticket.title}
      </div>
      <div
        title={ticket.note}
        className="box-content h-[32px] flex-none line-clamp-2 border-t border-border pt-1.5 font-mono text-[10.5px] leading-normal text-text-secondary [overflow-wrap:anywhere]"
      >
        {ticket.note}
      </div>
      {ticket.attempt && wrong ? (
        <div className="mt-auto flex h-[15px] flex-none items-center gap-1.5 text-[10.5px] font-medium" style={{ color: wrong }}>
          <Dot color={wrong} />
          <span className="truncate" title={`${ticket.attempt.by} ${ticket.attempt.label}`}>
            {INTENT_TEXT[ticket.attempt.intent].ticket}: {ticket.attempt.by} {ticket.attempt.label}
          </span>
        </div>
      ) : (
        <div className="mt-auto flex h-[15px] flex-none items-center gap-1.5 text-[10.5px] text-text-muted">
          <Dot color={ticket.holder ? holderColor : ticket.moved ? "#6cc596" : "#3a3a37"} />
          <span className="truncate">{ticket.holder ? `held by ${ticket.holder}` : ticket.moved ? "moved by the server" : "unclaimed"}</span>
        </div>
      )}
    </div>
  );
}

function Pod({ pod, color, laneName }: { pod: PodView; color: string; laneName: string | undefined }) {
  const tone = TONE[pod.tone];
  // The current pod is outlined in its own colour, or in the tone of a mistake when its move was one.
  const ring = pod.mark && pod.mark !== "finding" ? TONE[INTENT_TONE[pod.mark]] : color;
  return (
    <div
      className="flex h-[150px] flex-col gap-2 overflow-hidden rounded-lg border border-border bg-bg-secondary px-3 py-2.5 transition-colors"
      style={{ borderColor: pod.current ? ring : undefined, boxShadow: pod.current ? `0 0 0 3px ${ring}26` : undefined }}
    >
      <div className="flex min-w-0 flex-nowrap items-center gap-2">
        <Dot color={color} />
        <span className="flex-none whitespace-nowrap text-[13px] font-semibold text-text-primary">{pod.name}</span>
        <span className="min-w-0 flex-auto truncate font-mono text-[11px] text-text-muted">{pod.kind === "person" ? "a person" : "an agent"}, works {laneName ?? "its lane"}</span>
        <span
          className="ml-auto flex-none whitespace-nowrap rounded-full px-2 py-px text-[11px]"
          style={{ border: `1px solid ${pod.badge ? tone : "#3a3a37"}66`, color: pod.badge ? tone : "#8f8b84" }}
        >
          {pod.badge ?? "idle"}
        </span>
      </div>
      <div title={pod.say ?? undefined} className="h-[35px] flex-none line-clamp-2 font-mono text-[11.5px] leading-normal text-[#c9c6c0] [overflow-wrap:anywhere]">
        {pod.say}
      </div>
      <div
        title={pod.verdict ?? undefined}
        className="box-content h-[35px] flex-none line-clamp-2 border-t border-border pt-1.5 font-mono text-[11.5px] leading-normal [overflow-wrap:anywhere]"
        style={{ color: pod.badge ? tone : "#8f8b84" }}
      >
        {pod.verdict}
      </div>
    </div>
  );
}

// The code that refused, rejected or found the current move, with what the server said. It keeps its height on moves
// with nothing to show, so the pods below never move.
function RulePanel({ rule }: { rule: RuleView | null }) {
  const tone = rule ? TONE[INTENT_TONE[rule.intent]] : "#3a3a37";
  return (
    <div
      style={{ height: RULE_HEIGHT, borderColor: rule ? `${tone}88` : undefined }}
      className="flex flex-col gap-1.5 overflow-hidden rounded-lg border border-border bg-bg-secondary px-3 py-2.5"
    >
      {rule ? (
        <>
          <div className="flex min-w-0 items-baseline gap-2 text-xs">
            <span className="flex-none font-semibold uppercase tracking-wide" style={{ color: tone }}>
              {INTENT_TEXT[rule.intent].rule}
            </span>
            <span className="min-w-0 truncate font-mono text-text-muted">
              {rule.file}:{rule.from}
            </span>
          </div>
          <pre className="min-h-0 flex-auto overflow-x-auto font-mono text-[11.5px] leading-[1.55] text-[#c9c6c0]">
            {dedent(rule.lines).map((line, i) => (
              <div key={i} className="flex gap-3">
                <span className="w-7 flex-none select-none text-right text-text-muted">{rule.from + i}</span>
                <span className="whitespace-pre">{line}</span>
              </div>
            ))}
          </pre>
          <div title={rule.message} className="line-clamp-2 flex-none border-t border-border pt-1.5 font-mono text-[11.5px] leading-normal [overflow-wrap:anywhere]" style={{ color: tone }}>
            {rule.message}
          </div>
        </>
      ) : (
        <div className="flex h-full items-center justify-center text-xs text-text-muted">
          Nothing was refused, rejected or found on this move.
        </div>
      )}
    </div>
  );
}

function dedent(lines: string[]): string[] {
  const indent = Math.min(...lines.filter((l) => l.trim()).map((l) => l.length - l.trimStart().length));
  return lines.map((l) => l.slice(Number.isFinite(indent) ? indent : 0));
}
