import { useLayoutEffect, useMemo, useRef } from "react";
import { lineOf } from "../sources";
import type { Step } from "../types";

interface Props {
  source: { file: string; code: string };
  current: Step | null;
}

// The range the code's font size may take.
const MIN_PX = 10;
const MAX_PX = 12;

// The words that declare a decision and its tracks, picked out in the code.
const TOKEN =
  /(\.(?:Switch|Scale|Gate|When|AtLeast|Yes|No|Unsure)\b)|(\[Trax[A-Za-z]*)|("[^"]*")|(\b(?:public|class|protected|override)\b)/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1] ? "tk-track" : m[2] ? "tk-attr" : m[3] ? "tk-string" : "tk-keyword";
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

export function CodePanel({ source, current }: Props) {
  const code = useMemo(() => source.code.replace(/\r/g, ""), [source.code]);
  const lines = useMemo(() => code.split("\n"), [code]);
  // Only the chain, from Junctions() to Resolve(), so it stays on screen while the highlight moves. The line
  // numbers stay the file's.
  const [first, last, indent] = useMemo(() => {
    const at = lines.findIndex((l) => l.includes("Junctions()"));
    const end = lines.findIndex((l, i) => i > at && l.includes(".Resolve()"));
    const [from, to] = at < 0 || end < 0 ? [0, lines.length] : [at, end + 1];
    const indent = Math.min(...lines.slice(from, to).filter((l) => l.trim()).map((l) => l.search(/\S/)));
    return [from, to, indent];
  }, [lines]);
  const highlighted = current ? lineOf(code, current) : -1;
  const tone = current?.state === "FAILED" ? "failed" : current?.state === "IN_PROGRESS" ? "running" : "ran";

  // Keep each line whole, the way an editor shows it: size the font so the longest line fits the panel's width.
  // Below the smallest size the panel scrolls sideways instead of wrapping.
  const pane = useRef<HTMLPreElement>(null);
  useLayoutEffect(() => {
    const el = pane.current;
    if (!el) return;
    const fit = () => {
      let size = MAX_PX;
      // Two passes: the padding and the line-number gutter do not all scale with the font.
      for (let pass = 0; pass < 2; pass++) {
        el.style.fontSize = `${size}px`;
        const ratio = el.clientWidth / el.scrollWidth;
        if (ratio >= 1) break;
        size = Math.max(MIN_PX, Math.floor(size * ratio * 10) / 10);
      }
      el.style.fontSize = `${size}px`;
    };
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(el);
    return () => observer.disconnect();
  }, [source.file]);

  return (
    <div className="panel code-panel">
      <div className="panel-title">
        Code <span className="file">{source.file}</span>
      </div>
      <pre ref={pane} className="code">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          return (
            <div key={i} className={i === highlighted ? `line highlight ${tone}` : "line"}>
              <span className="gutter">{i + 1}</span>
              <span className="text">{highlight(line.slice(indent))}</span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}
