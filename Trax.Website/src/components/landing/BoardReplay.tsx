"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import {
  ASSIGNMENT_STATES,
  clientColor,
  movesOf,
  viewAt,
  type BoardRecordings,
  type PodView,
  type TicketView,
  type Tone,
} from "@/lib/board-replay";

// The Board sample's agents, replayed a move at a time: the board the server holds, the pod each agent works in, and
// what the server answered. It plays itself once the first time it is on screen; Back and Next take over.
const TONE: Record<Tone, string> = { ok: "#5cb85c", bad: "#d97a7a", warn: "#d4a24c", info: "#7a9ec2", idle: "#8f8b84" };
const STRIP_TONE: Record<(typeof ASSIGNMENT_STATES)[number], Tone> = { Working: "info", Checking: "info", Accepted: "ok", Rejected: "warn" };
const PACE_MS = 2600;
// A card's and a lane's fixed heights, in pixels: each text block on a card has room for a set number of lines and is
// clamped to it, so a longer note can never run into the line below.
const CARD_HEIGHT = 160;
const LANE_CHROME = 70;

export default function BoardReplay({ recordings }: { recordings: BoardRecordings }) {
  const scenarios = recordings.scenarios;
  const [key, setKey] = useState(scenarios[0]?.key ?? "");
  const [move, setMove] = useState(0);
  const [playing, setPlaying] = useState(false);
  const scenario = scenarios.find((s) => s.key === key);
  const last = movesOf(scenario);
  const view = useMemo(() => viewAt(recordings, scenario, move), [recordings, scenario, move]);
  // Every lane is as tall as the most tickets any lane holds anywhere in the recording, so no lane ever grows.
  const laneHeight = useMemo(() => {
    let most = 1;
    for (const s of scenarios) {
      for (let m = 0; m <= movesOf(s); m++) {
        const v = viewAt(recordings, s, m);
        for (const l of recordings.lanes) most = Math.max(most, v.tickets.filter((t) => t.lane === l.key).length);
      }
    }
    return LANE_CHROME + most * (CARD_HEIGHT + 8);
  }, [recordings, scenarios]);

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
    <div ref={root} className="flex flex-col gap-3.5">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <label className="flex items-center gap-2 text-[13px] text-text-secondary">
          Scenario
          <select
            value={key}
            onChange={(e) => {
              autoplayed.current = true;
              setPlaying(false);
              setKey(e.target.value);
              setMove(0);
            }}
            className="min-h-11 rounded-md border border-border bg-bg-secondary px-2.5 text-sm text-text-primary outline-none focus:border-accent"
          >
            {scenarios.map((s) => (
              <option key={s.key} value={s.key}>
                {s.title}
              </option>
            ))}
          </select>
        </label>
        <div className="flex gap-2">
          <button onClick={() => step(move - 1)} className="min-h-11 rounded-md border border-border bg-bg-secondary px-4 text-sm text-text-primary">
            Back
          </button>
          <button onClick={() => step(move + 1)} className="min-h-11 rounded-md bg-accent px-[18px] text-sm font-medium text-white hover:bg-accent-hover">
            Next move
          </button>
          <button onClick={() => step(0)} className="min-h-11 rounded-md border border-border px-4 text-sm text-text-secondary">
            Start over
          </button>
        </div>
        <span className="font-mono text-xs text-text-muted">
          move {move} / {last}
        </span>
      </div>
      {/* Its own row, as tall as the longest caption the recording gives (177 characters, two lines at full width), so
          the board below never moves as captions change length. */}
      <p className="min-h-[7.5em] text-sm leading-normal text-signal md:min-h-[4.5em] lg:min-h-[3em]">
        {move === 0 ? scenario?.caption : view.caption}
      </p>

      {/* The board across the whole width, then the pods in one row, so the whole demo fits a laptop screen once the
          tabs are at the top. The other tabs show code; this one shows the clients. */}
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


      <div className="flex flex-wrap justify-center gap-x-10 gap-y-2 font-mono text-xs text-text-muted">
        <span className="flex items-center gap-2">
          <Arrow down color="#7a9ec2" />
          work package: the ticket, instructions, tools, a lease
        </span>
        <span className="flex items-center gap-2">
          <Arrow color="#5cb85c" />
          submit(evidence): the server re-runs it before anything moves
        </span>
      </div>

      <div>
        <div className="grid min-w-0 content-start gap-2 [grid-template-columns:repeat(auto-fit,minmax(200px,1fr))]">
          {view.pods.map((pod) => (
            <Pod key={pod.name} pod={pod} color={clientColor(recordings, pod.name)} laneName={recordings.lanes.find((l) => l.key === pod.lane)?.name} />
          ))}
        </div>
      </div>
    </div>
  );
}

function Dot({ color }: { color: string }) {
  return <span className="inline-block size-2 flex-none rounded-full" style={{ background: color }} />;
}

function Arrow({ down, color }: { down?: boolean; color: string }) {
  return (
    <svg width="12" height="14" viewBox="0 0 12 14" aria-hidden="true">
      <path d={down ? "M6 1v12M1 8l5 5 5-5" : "M6 13V1M1 6l5-5 5 5"} fill="none" stroke={color} strokeWidth="1.5" />
    </svg>
  );
}

function TicketCard({ ticket, holderColor }: { ticket: TicketView; holderColor: string }) {
  const chip = ticket.state === "Done" ? "#6fa86f" : ticket.live ? "#5cb85c" : "#8f8b84";
  return (
    <div
      style={{ height: CARD_HEIGHT }}
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
      <div className="mt-auto flex h-[15px] flex-none items-center gap-1.5 text-[10.5px] text-text-muted">
        <Dot color={ticket.holder ? holderColor : ticket.moved ? "#5cb85c" : "#3a3a37"} />
        <span className="truncate">{ticket.holder ? `held by ${ticket.holder}` : ticket.moved ? "moved by the server" : "unclaimed"}</span>
      </div>
    </div>
  );
}

function Pod({ pod, color, laneName }: { pod: PodView; color: string; laneName: string | undefined }) {
  const tone = TONE[pod.tone];
  return (
    <div
      className="flex h-[208px] flex-col gap-2 overflow-hidden rounded-lg border border-border bg-bg-secondary px-3 py-2.5 transition-colors"
      style={{ borderColor: pod.current ? color : undefined, boxShadow: pod.current ? `0 0 0 3px ${color}26` : undefined }}
    >
      <div className="flex min-w-0 flex-nowrap items-center gap-2">
        <Dot color={color} />
        <span className="flex-none whitespace-nowrap text-[13px] font-semibold text-text-primary">{pod.name}</span>
        <span className="min-w-0 flex-auto truncate font-mono text-[11px] text-text-muted">{pod.kind === "person" ? "a person" : "agent pod"}</span>
        <span
          className="ml-auto flex-none whitespace-nowrap rounded-full px-2 py-px text-[11px]"
          style={{ border: `1px solid ${pod.badge ? tone : "#3a3a37"}66`, color: pod.badge ? tone : "#8f8b84" }}
        >
          {pod.badge ?? "idle"}
        </span>
      </div>
      <div title={pod.package ?? undefined} className="h-[33px] flex-none line-clamp-2 font-mono text-[11px] leading-normal text-text-secondary [overflow-wrap:anywhere]">
        {pod.package ?? (pod.badge === "refused" ? "no package" : `idle · works ${laneName ?? "its lane"}`)}
      </div>
      <div className="flex gap-1">
        {ASSIGNMENT_STATES.map((s) => {
          const on = pod.assignment === s;
          const c = TONE[STRIP_TONE[s]];
          return (
            <span
              key={s}
              className="min-w-0 flex-1 truncate rounded py-0.5 text-center font-mono text-[9.5px]"
              style={{ border: `1px solid ${on ? c : "#2a2a28"}`, color: on ? c : "#5f5c57", background: on ? `${c}1f` : undefined }}
            >
              {s}
            </span>
          );
        })}
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
