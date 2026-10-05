import { useLayoutEffect, useRef } from "react";
import type { ConsoleLine } from "../types";

/** What the page saw, narrated as it arrived: each attempt, junction, answer and track, and each action. */
export function ConsolePanel({ lines }: { lines: ConsoleLine[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the console itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [lines.length]);

  return (
    <div className="panel console-panel">
      <div className="panel-title">Console</div>
      <div ref={box} className="console">
        {lines.length === 0 && <p className="empty">Pick a scenario and press Run.</p>}
        {lines.map((l) => (
          <div key={l.key} className={`console-line ${l.tone}`}>
            <span className="console-time">{(Math.max(0, l.t) / 1000).toFixed(1).padStart(4, " ")}s</span>
            {l.text}
          </div>
        ))}
      </div>
    </div>
  );
}
