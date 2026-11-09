"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import CodeFold from "./CodeFold";
import PhoneFrame from "./PhoneFrame";
import ReplayToolbar, { PrimaryButton, ToolButton } from "./ReplayToolbar";
import StepButtons from "./StepButtons";
import data from "@/data/feature-walk.json";
import { KINDS, WALK, accessLines, derivedOf, featureBlocks, inFeature, kindOf, type Kind, type Part, type Passenger, type WalkData, type WalkStep } from "@/lib/feature-walk";

// A flight check-in feature acted out a moment at a time: Ada's phone and the departures board, as the scenarios
// describe them, with the lines each moment acts out lit in the feature and the parts they became lit on
// the two machines Trax derived from it. Each phrase is coloured by what it becomes. The machines and labels are the
// ones `trax machine from-gherkin` wrote (see src/lib/feature-walk.ts); the screens are drawn from them, not recorded.
const walk = data as unknown as WalkData;
const PACE_MS = 2800;
const MUTED = "#3a3a37";
const MUTED_TEXT = "#8f8b84";
type Lit = Map<Part, { kind: Kind; now: boolean }>;

export default function FeatureWalk() {
  // 0 is before the first step; 1..WALK.length is the step just run.
  const [at, setAt] = useState(0);
  const [playing, setPlaying] = useState(false);
  const last = WALK.length;
  const step = at > 0 ? WALK[at - 1] : null;

  const running = playing && at < last;
  useEffect(() => {
    if (!running) return;
    const timer = setTimeout(() => setAt((a) => a + 1), at === 0 ? 700 : PACE_MS);
    return () => clearTimeout(timer);
  }, [running, at]);

  // Play once, the first time the demo is on screen, unless the reader prefers less motion.
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
        setPlaying(true);
      },
      { threshold: 0.3 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, []);

  const go = (to: number) => {
    autoplayed.current = true;
    setPlaying(false);
    setAt(Math.max(0, Math.min(last, to)));
  };

  // Every part lit so far keeps the colour it was lit in; the current moment's are bright.
  const lit = useMemo(() => {
    const seen: Lit = new Map();
    for (let i = 0; i < at; i++) {
      const s = WALK[i];
      for (const p of s.lit) if (!seen.has(p) || i === at - 1) seen.set(p, { kind: kindOf(s), now: i === at - 1 });
    }
    return seen;
  }, [at]);

  const derived = step ? derivedOf(walk, step.derived) : null;

  return (
    <div ref={root} className="flex flex-col gap-4">
      <ReplayToolbar
        caption={step?.says}
        hint="Play acts the feature out a moment at a time, on the machines derived from it. Back and Next step by hand."
        reserve="min-h-[4.5em] lg:min-h-[3em]"
      >
        <PrimaryButton onClick={() => (at >= last ? (go(0), setPlaying(true)) : setPlaying(true))} disabled={running}>
          {at >= last ? "Play again" : "Play"}
        </PrimaryButton>
        <StepButtons onBack={() => go(at - 1)} onNext={() => go(at + 1)} at={at} of={last} />
        <ToolButton onClick={() => go(0)} disabled={at === 0}>
          Start over
        </ToolButton>
        <span className="text-xs text-text-muted sm:ml-auto">Ada, flight TX101</span>
      </ReplayToolbar>

      {/* The phone beside the feature's scenarios, in the order the story reaches them, and the two machines drawn wide
          across the full width under them, so their labels read at full size. Everything stacks on a phone. */}
      <div className="grid items-stretch gap-4 lg:grid-cols-[264px_minmax(0,1fr)]">
        <Phone passenger={step?.ada ?? null} step={step} />
        <FeaturePanel at={at} />
      </div>
      <MachinePanel lit={lit} ada={step?.ada.state ?? null} flight={step?.flight ?? "Scheduled"} step={step} />

      <CodeFold label="The derived files and what the colours mean, lit as it runs" pulse={at} color={step ? KINDS[kindOf(step)].color : undefined}>
        <div className="flex flex-col gap-4">
          <ul className="flex flex-wrap gap-x-5 gap-y-2 text-xs text-text-muted">
            {(Object.keys(KINDS) as Kind[]).map((k) => (
              <li key={k} className="flex items-center gap-2">
                <span className="inline-block size-2.5 rounded-sm" style={{ background: KINDS[k].color }} />
                {KINDS[k].label}
              </li>
            ))}
          </ul>
          <div className="grid gap-4 sm:grid-cols-2">
            <AccessPanel lit={lit} />
            <div className="flex min-w-0 flex-col rounded-lg border border-border bg-bg-primary">
              <PanelTitle>{derived ? derived.file : "derived"}</PanelTitle>
              <pre
                className="min-h-[132px] flex-auto overflow-x-auto px-3 py-2 font-mono text-[11px] leading-relaxed [overflow-wrap:anywhere] whitespace-pre-wrap"
                style={{ color: step ? KINDS[kindOf(step)].color : MUTED_TEXT }}
              >
                {derived?.text ?? "The part of the derived files the current line produced."}
              </pre>
            </div>
          </div>
        </div>
      </CodeFold>
    </div>
  );
}

function PanelTitle({ children }: { children: React.ReactNode }) {
  return <div className="border-b border-border px-3 py-2 font-mono text-[11px] text-text-muted">{children}</div>;
}

const KEYWORDS = /^(\s*)(Feature:|Background:|Scenario:|Given|When|Then|And)(.*)$/;

// The feature as written, every line the story uses, one card per scenario and the Background first, side by side.
// Each phrase the story acts out is in its kind's colour; the card and lines of the current moment are marked.
function FeaturePanel({ at }: { at: number }) {
  const step = at > 0 ? WALK[at - 1] : null;
  const current = step ? step.marks.map((m) => m.line) : [];
  const ring = step ? KINDS[kindOf(step)].color : null;
  const marks = new Map<number, { phrase: string; kind: Kind }>();
  for (const s of WALK) for (const m of s.marks) if (!marks.has(m.line)) marks.set(m.line, { phrase: inFeature(m.phrase), kind: m.kind });
  return (
    <div className="min-w-0 rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>{walk.feature.file}</PanelTitle>
      <div className="grid gap-3 p-3 md:grid-cols-2 xl:grid-cols-3">
        {featureBlocks(walk).map((block) => {
          const on = block.some((l) => current.includes(l.n));
          return (
            <div
              key={block[0].n}
              className="min-w-0 rounded-md border bg-bg-primary py-1.5 transition-colors"
              style={{ borderColor: on && ring ? `${ring}99` : "var(--border)" }}
            >
              {block.map(({ n, text }) => (
                <FeatureLine key={n} n={n} text={text} on={current.includes(n)} mark={marks.get(n)} />
              ))}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function FeatureLine({ n, text, on, mark }: { n: number; text: string; on: boolean; mark?: { phrase: string; kind: Kind } }) {
  const m = KEYWORDS.exec(text);
  const body = (m ? m[3] : text).trimStart();
  const color = mark ? KINDS[mark.kind].color : undefined;
  let rest: React.ReactNode = body;
  if (mark && body.includes(mark.phrase)) {
    const i = body.indexOf(mark.phrase);
    rest = (
      <>
        {body.slice(0, i)}
        <span
          className="rounded-sm px-0.5 font-semibold"
          style={{ color, background: on ? `${color}2e` : undefined, boxShadow: on ? `0 0 0 1px ${color}88` : undefined }}
        >
          {mark.phrase}
        </span>
        {body.slice(i + mark.phrase.length)}
      </>
    );
  }
  const heading = m?.[2].endsWith(":");
  return (
    <div className={`flex gap-2 pr-2 font-mono text-[11px] leading-[1.45] transition-colors ${on ? "bg-bg-hover" : ""}`}>
      <span className={`w-7 flex-none select-none text-right ${on ? "text-text-primary" : "text-text-muted"}`}>{n}</span>
      <span className={`min-w-0 text-text-secondary [overflow-wrap:anywhere] ${heading || !m ? "" : "pl-3"}`}>
        {m ? (
          <span className={heading ? "text-text-primary" : "font-semibold text-text-primary"}>{m[2]} </span>
        ) : null}
        {rest}
      </span>
    </div>
  );
}

type Box = { x: number; y: number; w: number; h: number };
// Drawn wide and short, so it spans the demo's width: the flight on top, Ada's booking in one row under it, and where
// the booking can end up below that.
// Only the parts of the two machines this walk lights, on one grid: the flight's first move on top, Ada's booking under
// it with the flight's Open straight above the booking's, so the policy is an arrow down. The rest of each machine is
// in its IR, in the fold below and in the sample.
const C = [250, 490, 730, 970];
const W = 130;
const at = (col: number, y: number, h = 48): Box => ({ x: C[col] - W / 2, y, w: W, h });
const FLIGHT: Record<string, Box> = {
  Scheduled: at(0, 20),
  Open: at(1, 20),
};
const BOOKING: Record<string, Box> = {
  Booked: at(0, 128),
  Open: at(1, 128),
  Issuing: at(2, 122, 60),
  CheckedIn: at(3, 128),
};
const L = (col: number) => C[col] - W / 2;
const R = (col: number) => C[col] + W / 2;
const mid = (a: number, b: number) => (R(a) + L(b)) / 2;

// The parts of flight.ir.json and booking.ir.json the walk reaches, and the policy from host.json between them.
function MachinePanel({ lit, ada, flight, step }: { lit: Lit; ada: string | null; flight: string; step: WalkStep | null }) {
  const who = (s: string) => (ada === s ? "Ada" : "");
  const color = (p: Part) => {
    const l = lit.get(p);
    return l ? KINDS[l.kind].color : MUTED;
  };
  const strong = (p: Part) => lit.get(p)?.now ?? false;
  const text = (p: Part) => (lit.has(p) ? color(p) : MUTED_TEXT);
  const label = { fontSize: 14, textAnchor: "middle" as const };
  const fy = 44;
  const by = 152;
  return (
    <div className="min-w-0 rounded-lg border border-border bg-bg-secondary">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 border-b border-border px-3 py-1.5">
        <span className="font-mono text-[11px] text-text-muted">
          flight.ir.json, booking.ir.json · derived, not written · the parts this walk uses
        </span>
        <Departures flight={flight} step={step} />
      </div>
      <svg viewBox="0 0 1100 216" className="block h-auto w-full" role="img" aria-label="The flight opening check-in, and Ada's booking from Booked to CheckedIn">
        <text x={24} y={fy + 5} fontSize="15" fill={MUTED_TEXT}>the flight</text>
        <text x={24} y={by + 5} fontSize="15" fill={MUTED_TEXT}>Ada&apos;s booking</text>

        <Edge x1={R(0)} y1={fy} x2={L(1)} y2={fy} color={color("edge:f:OpenCheckIn")} strong={strong("edge:f:OpenCheckIn")} />
        <text {...label} x={mid(0, 1)} y={fy - 10} fill={text("edge:f:OpenCheckIn")}>check-in opens</text>

        {/* The policy: the flight entering Open opens every booking still Booked. */}
        <Edge x1={C[1]} y1={68} x2={C[1]} y2={128} color={lit.has("policy:open") ? KINDS.policy.color : "#4a4844"} strong={strong("policy:open")} dashed />
        <text x={C[1] + 14} y={103} fontSize="14" fill={lit.has("policy:open") ? KINDS.policy.color : MUTED_TEXT}>
          and opens every Booked booking
        </text>

        <Edge x1={R(0)} y1={by} x2={L(1)} y2={by} color={color("edge:b:OpenCheckIn")} strong={strong("edge:b:OpenCheckIn")} />
        <text {...label} x={mid(0, 1)} y={by - 10} fill={text("edge:b:OpenCheckIn")}>opens</text>
        {/* GivePassport: from Open back to Open, storing the number. */}
        <path d={`M${C[1] - 30} 176 C ${C[1] - 30} 206, ${C[1] + 30} 206, ${C[1] + 30} 184`} fill="none" stroke={color("edge:b:GivePassport")} strokeWidth="1.8" />
        <Head x={C[1] + 30} y={176} dx={0} dy={-1} color={color("edge:b:GivePassport")} />
        <text x={C[1] + 40} y={204} fontSize="13.5" fill={text("edge:b:GivePassport")}>gives passport</text>
        <Edge x1={R(1)} y1={by} x2={L(2)} y2={by} color={color("edge:b:CheckIn")} strong={strong("edge:b:CheckIn")} />
        <text {...label} x={mid(1, 2)} y={by - 10} fontWeight={600} fill={text("edge:b:CheckIn")}>checks in</text>
        <text {...label} x={mid(1, 2)} y={by + 22} fontSize={13.5} fill={text("guard:CheckIn")}>passport in</text>
        <Edge x1={R(2)} y1={by} x2={L(3)} y2={by} color={color("edge:b:issued")} strong={strong("edge:b:issued")} />
        <text {...label} x={mid(2, 3)} y={by - 10} fill={text("edge:b:issued")}>accepted</text>

        {Object.entries(FLIGHT).map(([s, b]) => (
          <State key={`f${s}`} name={s} box={b} part={`flight:${s}` as Part} marker={flight === s ? "TX101" : ""} lit={lit} />
        ))}
        {Object.entries(BOOKING).map(([s, b]) => (
          <State
            key={`b${s}`}
            name={s}
            box={b}
            part={`booking:${s}` as Part}
            marker={who(s)}
            lit={lit}
            sub={s === "Issuing" ? { text: "runs IssueBoardingPass", color: text("invoke:Issuing") } : undefined}
          />
        ))}
      </svg>
    </div>
  );
}

function State({
  name,
  box: b,
  part,
  lit,
  marker,
  sub,
}: {
  name: string;
  box: Box;
  part: Part;
  lit: Lit;
  /** Who is in this state now, or "". */
  marker: string;
  sub?: { text: string; color: string };
}) {
  const here = marker !== "";
  const l = lit.get(part);
  const c = l ? KINDS[l.kind].color : here ? KINDS.state.color : MUTED;
  return (
    <g>
      <rect x={b.x} y={b.y} width={b.w} height={b.h} rx={7} fill={here ? `${KINDS.state.color}1f` : "#161715"} stroke={c} strokeWidth={l?.now || here ? 2.2 : 1.4} />
      <text x={b.x + b.w / 2} y={b.y + (here || sub ? 20 : b.h / 2 + 5)} textAnchor="middle" fontSize="15.5" fontWeight="600" fill={l || here ? "#e8e6e1" : MUTED_TEXT}>
        {name}
      </text>
      {sub && (
        <text x={b.x + b.w / 2} y={b.y + 37} textAnchor="middle" fontSize="11.5" fill={sub.color}>
          {sub.text}
        </text>
      )}
      {here && (
        <text x={b.x + b.w / 2} y={b.y + b.h - 7} textAnchor="middle" fontSize="12" fill={KINDS.state.color}>
          {marker}
        </text>
      )}
    </g>
  );
}

function Edge({ x1, y1, x2, y2, color, strong, dashed }: { x1: number; y1: number; x2: number; y2: number; color: string; strong?: boolean; dashed?: boolean }) {
  const len = Math.hypot(x2 - x1, y2 - y1);
  const dx = (x2 - x1) / len;
  const dy = (y2 - y1) / len;
  return (
    <>
      <line x1={x1} y1={y1} x2={x2 - dx * 6} y2={y2 - dy * 6} stroke={color} strokeWidth={strong ? 2.25 : 1.5} strokeDasharray={dashed ? "5 4" : undefined} />
      <Head x={x2} y={y2} dx={dx} dy={dy} color={color} />
    </>
  );
}

function Head({ x, y, dx, dy, color }: { x: number; y: number; dx: number; dy: number; color: string }) {
  const bx = x - dx * 8;
  const by = y - dy * 8;
  return <polygon points={`${x},${y} ${bx - dy * 4},${by + dx * 4} ${bx + dy * 4},${by - dx * 4}`} fill={color} />;
}

// Every line of access.txt: who may start, send each command and read, each derived from the scenarios.
function AccessPanel({ lit }: { lit: Lit }) {
  return (
    <div className="min-w-0 rounded-lg border border-border bg-bg-primary">
      <PanelTitle>{walk.access.file}</PanelTitle>
      <ul className="px-3 py-2 font-mono text-[11.5px] leading-[1.8]">
        {accessLines(walk).map((l) => {
          const on = lit.get(`access:${l}` as Part);
          const c = on ? KINDS[on.kind].color : undefined;
          return (
            <li key={l} className="rounded-sm px-1" style={{ color: c ?? MUTED_TEXT, background: on?.now ? `${c}24` : undefined, fontWeight: on ? 600 : undefined }}>
              {l}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

// Ada's app, as the scenarios describe it, in a phone: the bookings view's status and seat, the passport field and
// the Check in button the machine offers in the booking's state, and what the server last told her.
function Phone({ passenger, step }: { passenger: Passenger | null; step: WalkStep | null }) {
  const fields = walk.glossary.glossary.views.bookings?.fields;
  const state = passenger?.state ?? null;
  const label = state ? fields?.status.labels?.[state] : null;
  // The moves the derived machine has from this state: the app offers Check in only where there is one.
  const canCheckIn = state ? walk.machines.booking.ir.transitions.some((t) => t.from === state && t.trigger === "CheckIn") : false;
  const acting = step?.actor === "Ada";
  const ring = acting && step ? KINDS[kindOf(step)].color : null;
  const pressed = acting && step?.lit.includes("edge:b:CheckIn");
  const seat = state === "CheckedIn" ? passenger?.seat ?? null : null;
  const viewLit = acting && step?.lit.includes("view:bookings");
  return (
    <PhoneFrame ring={ring} height="h-[440px] lg:h-full lg:min-h-[420px]">
        <div className="flex flex-1 flex-col gap-4 px-4 pb-4 pt-3">
          <div className="flex items-baseline justify-between">
            <span className="text-sm font-semibold text-text-primary">My trips</span>
            <span className="font-mono text-xs text-text-muted">{state ? "Ada" : ""}</span>
          </div>
          {state === null ? (
            <div className="flex flex-1 items-center justify-center text-sm text-text-muted">Not signed in</div>
          ) : (
            <div className="flex flex-col gap-3 rounded-xl border border-border bg-bg-primary p-3">
              <div className="flex items-baseline justify-between">
                <span className="font-mono text-sm text-text-primary">TX101</span>
                <span className="text-[10px] uppercase tracking-wider text-text-muted">booking</span>
              </div>
              <div className="text-base font-semibold transition-colors" style={{ color: viewLit ? KINDS.view.color : "#e8e6e1" }}>
                {label}
              </div>
              {seat ? (
                <div className="rounded-lg border border-dashed px-3 py-3 text-center" style={{ borderColor: `${KINDS.view.color}88` }}>
                  <div className="text-[10px] uppercase tracking-wider text-text-muted">Boarding pass</div>
                  <div className="mt-1 font-mono text-2xl font-semibold" style={{ color: KINDS.view.color }}>
                    {seat}
                  </div>
                  <div className="text-[10px] text-text-muted">seat</div>
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  <div className={`rounded-md border border-border bg-bg-secondary px-3 py-1.5 font-mono text-sm ${canCheckIn ? "" : "opacity-40"}`}>
                    {passenger?.passport ? <span className="text-text-primary">{passenger.passport}</span> : <span className="text-text-muted">Passport number</span>}
                  </div>
                  <div className={`rounded-md border border-border bg-bg-secondary px-3 py-1.5 font-mono text-sm ${canCheckIn ? "" : "opacity-40"}`}>
                    <span className="text-text-muted">Seat </span>
                    <span className="text-text-primary">{passenger?.seat ?? "14C"}</span>
                  </div>
                  <button
                    type="button"
                    tabIndex={-1}
                    disabled={!canCheckIn}
                    className="rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-30"
                    style={{
                      background: canCheckIn ? (pressed ? KINDS.command.color : "#2f3a2e") : "#262624",
                      color: canCheckIn ? (pressed ? "#1a1406" : "#e8e6e1") : MUTED_TEXT,
                    }}
                  >
                    Check in
                  </button>
                </div>
              )}
              {passenger?.told ? (
                <div className="rounded-md border px-3 py-1.5 text-xs" style={{ borderColor: `${KINDS.guard.color}88`, color: KINDS.guard.color }}>
                  {passenger.told}
                </div>
              ) : null}
            </div>
          )}
        </div>
    </PhoneFrame>
  );
}

// The departures board: the departures view of the one flight, which every passenger reads, in the machines' title bar.
function Departures({ flight, step }: { flight: string; step: WalkStep | null }) {
  const label = walk.glossary.glossary.views.departures?.fields.status.labels?.[flight] ?? flight;
  const lit = step?.lit.includes("view:departures");
  const moved = step?.actor === "server" && step.lit.some((p) => p.startsWith("flight:") || p.startsWith("edge:f:"));
  const ring = lit || moved ? KINDS[kindOf(step as WalkStep)].color : null;
  return (
    <span
      className="flex items-center gap-3 rounded-md border bg-[#111210] px-3 py-1 font-mono text-[12px] transition-colors"
      style={{ borderColor: ring ?? "#2a2a28" }}
    >
      <span className="font-sans text-[12px] font-semibold text-text-primary">Departures</span>
      <span className="text-text-primary">TX101</span>
      <span className="transition-colors" style={{ color: lit ? KINDS.view.color : "#e8c46a" }}>
        {label}
      </span>
    </span>
  );
}
