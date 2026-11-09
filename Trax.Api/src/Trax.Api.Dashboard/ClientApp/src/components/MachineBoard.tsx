import { useMemo, useState } from "react";
import {
  ACTOR_H,
  hasProvenance,
  layoutBoard,
  neighbours,
  type Board,
  type BoardArrow,
  type BoardNote,
} from "../lib/machineBoard";

// Event storming's paper colours. They stay the same in the dark theme, as paper on a wall would, and the role is
// written on every note, so colour never carries it alone.
const PAPER: Record<string, { paper: string; role: string }> = {
  actor: { paper: "#fff3a6", role: "Actor" },
  command: { paper: "#a9d4ff", role: "Command" },
  event: { paper: "#ffb877", role: "Event" },
  policy: { paper: "#d9c4ff", role: "Policy" },
  aggregate: { paper: "#ffe066", role: "Machine" },
  view: { paper: "#b9eeb0", role: "View" },
};
const INK = "#1f2328";
const LEGEND = ["actor", "command", "event", "policy", "aggregate", "view"] as const;

// A slight tilt per note, the same every render, so the wall reads as paper without moving under the reader.
function tilt(key: string): number {
  let h = 0;
  for (const c of key) h = (h * 31 + c.charCodeAt(0)) | 0;
  return ((Math.abs(h) % 7) - 3) * 0.35;
}

/**
 * The event-storming board of a machine served from features, drawn as paper notes on a wall: one lane per machine
 * it joins, read left to right. Clicking a note lights up what it touches and lists the scenarios that put it there,
 * by file, line and name; a host file with no provenance draws the board without them.
 */
export function MachineBoard({ board, initialStates }: { board: Board; initialStates?: Record<string, string> }) {
  const [selected, setSelected] = useState<string | null>(null);
  const layout = useMemo(() => layoutBoard(board, initialStates), [board, initialStates]);
  const near = useMemo(() => (selected ? neighbours(board, selected) : null), [board, selected]);
  const sticky = board.stickies.find((s) => s.id === selected);
  const provenance = hasProvenance(board);

  if (layout.notes.length === 0) return <p className="text-sm text-muted">{board.machine} has nothing on its board.</p>;

  return (
    <div>
      <ul aria-label="What each colour is" className="flex flex-wrap gap-3 mb-3 text-xs text-fg-2">
        {LEGEND.map((kind) => (
          <li key={kind} className="flex items-center gap-1.5">
            <span aria-hidden className="inline-block w-3.5 h-3.5 rounded-[2px]" style={{ background: PAPER[kind].paper }} />
            {PAPER[kind].role}
          </li>
        ))}
      </ul>

      <div className="overflow-auto rounded-lg border border-line bg-sunken" role="group" aria-label={`Board of ${board.machine}`}>
        <div className="relative" style={{ width: layout.width, height: layout.height }}>
          {layout.lanes.map((lane, i) => (
            <div
              key={lane.machine}
              aria-hidden
              className={`absolute left-0 right-0 ${i > 0 ? "border-t border-dashed border-line-strong" : ""}`}
              style={{ top: lane.y, height: lane.h }}
            >
              {layout.lanes.length > 1 && (
                <span className="absolute right-3 top-2 text-[11px] uppercase tracking-wider text-muted">{lane.machine}</span>
              )}
            </div>
          ))}
          <Arrows arrows={layout.arrows} width={layout.width} height={layout.height} near={near} selected={selected} />
          {layout.notes.map((note) => (
            <Note
              key={note.key}
              note={note}
              selected={note.sticky.id === selected}
              faded={near != null && !near.has(note.sticky.id)}
              onClick={() => setSelected(note.sticky.id === selected ? null : note.sticky.id)}
            />
          ))}
        </div>
      </div>

      {!provenance && (
        <p className="mt-2 text-xs text-muted">
          This host file records no scenarios, so the board does not say which ones produced each note.
        </p>
      )}
      {sticky && provenance && (
        <div aria-label="Scenarios behind it" className="mt-3 text-sm">
          <p className="font-medium text-fg">
            {sticky.label} <span className="text-muted">({PAPER[sticky.kind]?.role.toLowerCase() ?? sticky.kind})</span>
          </p>
          {sticky.scenarios.length === 0 ? (
            <p className="text-muted">No scenario produced this note on its own.</p>
          ) : (
            <ul className="mt-1 space-y-0.5 text-fg-2">
              {sticky.scenarios.map((s) => (
                <li key={`${s.file}:${s.line}`}>
                  <span className="font-mono text-xs">
                    {s.file}:{s.line}
                  </span>{" "}
                  {s.scenario}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function Note({ note, selected, faded, onClick }: { note: BoardNote; selected: boolean; faded: boolean; onClick: () => void }) {
  const { sticky } = note;
  const look = PAPER[sticky.kind] ?? { paper: "#e5e7eb", role: sticky.kind };
  const small = note.h <= ACTOR_H;
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      aria-label={`${sticky.label}, ${look.role.toLowerCase()}`}
      title={sticky.label}
      className={`absolute flex flex-col text-left rounded-[3px] transition-[opacity,box-shadow] duration-150 focus:outline-none focus-visible:ring-2 focus-visible:ring-accent ${
        selected ? "ring-2 ring-accent z-10" : ""
      } ${faded ? "opacity-35" : ""} ${small ? "px-2 py-1 justify-center" : "p-2"}`}
      style={{
        left: note.x,
        top: note.y,
        width: note.w,
        height: note.h,
        background: look.paper,
        color: INK,
        transform: `rotate(${tilt(note.key)}deg)`,
        boxShadow: "1px 2px 0 rgba(0,0,0,0.18), 0 6px 12px -6px rgba(0,0,0,0.35)",
      }}
    >
      {!small && <span className="text-[9.5px] font-semibold uppercase tracking-wider opacity-60">{look.role}</span>}
      <span className={`${small ? "text-[11px] leading-tight line-clamp-2" : "mt-1 text-[13px] leading-snug line-clamp-4"} font-medium break-words`}>
        {sticky.label}
      </span>
    </button>
  );
}

function Arrows({
  arrows,
  width,
  height,
  near,
  selected,
}: {
  arrows: BoardArrow[];
  width: number;
  height: number;
  near: Set<string> | null;
  selected: string | null;
}) {
  return (
    <svg aria-hidden className="absolute inset-0 pointer-events-none text-muted" width={width} height={height}>
      <defs>
        <marker id="board-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0,0 L8,4 L0,8 z" fill="currentColor" />
        </marker>
      </defs>
      {arrows.map((a) => {
        const lit = near != null && near.has(a.from.sticky.id) && near.has(a.to.sticky.id);
        // An arrow across columns or lanes would cross other notes: it is drawn only for the note it touches.
        const touches = selected != null && (a.from.sticky.id === selected || a.to.sticky.id === selected);
        if (!a.local && !touches) return null;
        return (
          <path
            key={a.key}
            d={pathOf(a)}
            fill="none"
            stroke="currentColor"
            strokeWidth={lit ? 2 : 1.25}
            opacity={near == null ? 0.7 : lit ? 1 : 0.15}
            markerEnd="url(#board-arrow)"
          />
        );
      })}
    </svg>
  );
}

/** Straight down a column, or a curve from one note's right edge to the next one's left. */
function pathOf({ from, to, local }: BoardArrow): string {
  if (local) {
    const down = to.y > from.y;
    const x = from.x + from.w / 2;
    const y1 = down ? from.y + from.h : from.y;
    const y2 = down ? to.y - 2 : to.y + to.h + 2;
    return `M${x},${y1} L${x},${y2}`;
  }
  if (from.x === to.x) {
    // The same column of another lane: out of the right edge, round the gap, and back in.
    const x = from.x + from.w;
    const y1 = from.y + from.h / 2;
    const y2 = to.y + to.h / 2;
    return `M${x},${y1} C${x + 40},${y1} ${x + 40},${y2} ${x + 2},${y2}`;
  }
  const forward = to.x > from.x;
  const x1 = forward ? from.x + from.w : from.x;
  const x2 = forward ? to.x - 2 : to.x + to.w + 2;
  const y1 = from.y + from.h / 2;
  const y2 = to.y + to.h / 2;
  const bend = Math.max(24, Math.abs(x2 - x1) / 2);
  const c1 = forward ? x1 + bend : x1 - bend;
  const c2 = forward ? x2 - bend : x2 + bend;
  return `M${x1},${y1} C${c1},${y1} ${c2},${y2} ${x2},${y2}`;
}
