import { useEffect, useRef, useState } from "react";
import type { MachineView, MoveView } from "./machines";

/**
 * A machine's states in a row, with its moves as labelled arrows. The current state glows, the moves out of
 * it are marked as available, and the arrow the last move took lights up, so a transition the server made
 * is visible as it happens. A move between states that are not neighbours is drawn as a loop underneath.
 */
export function Diagram({ machine, current }: { machine: MachineView; current: string | null }) {
  const previous = useRef<string | null>(null);
  const [moved, setMoved] = useState<{ from: string; to: string } | null>(null);

  useEffect(() => {
    const from = previous.current;
    previous.current = current;
    if (!from || !current || from === current) return;
    setMoved({ from, to: current });
    const t = setTimeout(() => setMoved(null), 1600);
    return () => clearTimeout(t);
  }, [current]);

  const { states, moves } = machine;
  const between = (a: string, b: string) => moves.find((m) => m.from === a && m.to === b);
  const neighbours = (m: MoveView) =>
    Math.abs(states.findIndex((s) => s.name === m.from) - states.findIndex((s) => s.name === m.to)) === 1;
  const loops = moves.filter((m) => !neighbours(m));
  const index = (name: string | null) => states.findIndex((s) => s.name === name);

  const label = (m: MoveView | undefined, dir: "fwd" | "back") => {
    if (!m) return null;
    const lit = moved?.from === m.from && moved?.to === m.to;
    const available = current === m.from;
    return (
      <span className={`edge-label ${lit ? "lit" : ""} ${available ? "avail" : ""} ${m.effect ? "edge-effect" : ""}`}>
        {dir === "back" && <span className="arrow">←</span>}
        {m.trigger}
        {m.guard && !m.effect && <span className="guard-flag" title={`Guard: ${m.guard}`}>guard</span>}
        {m.effect && <span className="effect-flag">{m.effect} · once</span>}
        {dir === "fwd" && <span className="arrow">→</span>}
      </span>
    );
  };

  return (
    <div className="diagram">
      <div className="diagram-row">
        {states.map((state, i) => {
          const next = states[i + 1];
          return (
            <div key={state.name} className="diagram-cell">
              <div
                className={[
                  "node",
                  state.name === current ? "node-current" : "",
                  current && index(current) > i ? "node-passed" : "",
                  state.committed ? "node-committed" : "",
                ].join(" ")}
              >
                {state.name}
                {state.committed && <span className="node-flag">committed</span>}
              </div>
              {next && (
                <div className="edge">
                  {label(between(state.name, next.name), "fwd")}
                  <span className="edge-line" />
                  {label(between(next.name, state.name), "back")}
                </div>
              )}
            </div>
          );
        })}
      </div>
      {loops.map((m) => (
        <div
          key={m.trigger}
          className={`loop ${current === m.from ? "avail" : ""} ${moved?.from === m.from && moved?.to === m.to ? "lit" : ""}`}
        >
          <span className="loop-arrow">↺</span>
          <span className="loop-trigger">{m.trigger}</span>
          <span className="loop-path">
            {m.from} → {m.to}
          </span>
        </div>
      ))}
    </div>
  );
}
