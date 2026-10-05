import { useLayoutEffect, useMemo, useRef } from "react";
import { lineOf } from "../sources";
import type { Step } from "../types";

interface Props {
  source: { file: string; code: string };
  current: Step | null;
}

// The range the code's font size may take.
const MIN_PX = 10;
const MAX_PX = 15;

export function CodePanel({ source, current }: Props) {
  const code = useMemo(() => source.code.replace(/\r/g, ""), [source.code]);
  const lines = useMemo(() => code.split("\n"), [code]);
  // Show the chain itself, from Junctions() to Resolve(), dedented, so it fits the panel unwrapped and never
  // has to scroll to follow a run: the highlight moves, the code stays put. Line numbers stay the file's.
  const [first, last, indent] = useMemo(() => {
    const at = lines.findIndex((l) => l.includes("Junctions()"));
    const end = lines.findIndex((l, i) => i > at && l.includes(".Resolve()"));
    const [from, to] = at < 0 || end < 0 ? [0, lines.length] : [at, end + 1];
    const indent = Math.min(...lines.slice(from, to).filter((l) => l.trim()).map((l) => l.search(/\S/)));
    return [from, to, indent];
  }, [lines]);
  const highlighted = current ? lineOf(code, current) : -1;
  const tone = current?.state === "FAILED" ? "failed" : current?.state === "IN_PROGRESS" ? "running" : "ran";

  // Size the font so the whole train fits the panel without wrapping or scrolling: the largest size in range
  // at which nothing overflows. Below the smallest size, the panel scrolls instead.
  const pane = useRef<HTMLPreElement>(null);
  const body = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const fit = () => {
      const p = pane.current;
      const b = body.current;
      if (!p || !b) return;
      const style = getComputedStyle(p);
      const width = p.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
      const height = p.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);
      const fits = (px: number) => {
        b.style.fontSize = `${px}px`;
        return b.scrollHeight <= height && b.scrollWidth <= width + 1;
      };
      let low = MIN_PX;
      let high = MAX_PX;
      if (fits(high)) low = high;
      else for (let i = 0; i < 7; i++) {
        const mid = (low + high) / 2;
        if (fits(mid)) low = mid;
        else high = mid;
      }
      b.style.fontSize = `${Math.floor(low * 10) / 10}px`;
    };
    fit();
    const observer = new ResizeObserver(fit);
    if (pane.current) observer.observe(pane.current);
    return () => observer.disconnect();
  }, [first, last, code]);

  return (
    <div className="code-panel">
      <div className="file">{source.file}</div>
      <pre ref={pane}>
        <div ref={body} className="code-body">
          {lines.slice(first, last).map((line, offset) => {
            const i = first + offset;
            return (
              <div key={i} className={i === highlighted ? `line highlight ${tone}` : "line"}>
                <span className="gutter">{i + 1}</span>
                <span className="text">{line.slice(indent) || " "}</span>
              </div>
            );
          })}
        </div>
      </pre>
    </div>
  );
}
