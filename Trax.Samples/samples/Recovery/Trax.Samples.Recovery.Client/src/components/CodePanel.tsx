import { useMemo, useRef } from "react";
import { alignChains, commonIndent, hangingIndent, shownRange } from "../code";
import type { Source } from "../sources";
import { useFitCode } from "../useFitCode";

// The words picked out in the code: what declares a step, a decision and its tracks, a machine's states, events,
// timer and the train it runs; attributes; strings; comments; and a few keywords.
const TOKEN =
  /(\.(?:Switch|Scale|Gate|When|AtLeast|Yes|No|Unsure|In|On|Invokes|After|GoTo|To|OnDone|OnFailed|OnCancelled|Chain|Checkpoint|Parallel|Branch|Resolve)\b|\bChain(?=<))|(\[Trax[A-Za-z]*)|("[^"]*")|(\/\/.*$)|(\b(?:public|class|protected|override)\b)/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1] ? "tk-track" : m[2] ? "tk-attr" : m[3] ? "tk-string" : "tk-muted";
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

/** A machine's states: from its first m.In(...) to the last chained statement after it, `.OnCancelled(...);` here. */
function machineRange(lines: string[]): [number, number] {
  const at = lines.findIndex((l) => l.trim().startsWith("m.In("));
  let end = -1;
  lines.forEach((l, i) => {
    if (at >= 0 && i > at && /^\s*\.\w+\(.*\);\s*$/.test(l)) end = i;
  });
  return at < 0 || end < 0 ? [0, lines.length] : [at, end + 1];
}

export interface CodeTab {
  key: string;
  source: Source;
  /** Which part of the file to show: a train's chain, from Junctions() to Resolve(), or a machine's states. */
  part: "chain" | "machine";
}

interface Props {
  tabs: CodeTab[];
  shown: string;
  /** Set to let the reader flip the file tabs by hand. */
  onPick?: (key: string) => void;
  /** The line to light, 0-based in the file, and its colour. */
  lit: { line: number; color: string } | null;
  /** Words at the right of the title bar. */
  aside?: React.ReactNode;
}

/**
 * A file's code, the part that acts, with the acting line lit. The chains are re-indented so the step names line up,
 * the font fits the longest line between 12 and 11 pixels, and a line still too long wraps under itself; the panel
 * never scrolls sideways. The line numbers stay the file's.
 */
export function CodePanel({ tabs, shown, onPick, lit, aside }: Props) {
  const tab = tabs.find((t) => t.key === shown) ?? tabs[0];
  const lines = useMemo(() => alignChains(tab.source.code.split("\n")), [tab.source.code]);
  const [first, last, indent] = useMemo(() => {
    const [from, to] =
      tab.part === "chain"
        ? shownRange(lines, (l) => l.includes("Junctions()"), (l) => l.includes(".Resolve()"))
        : machineRange(lines);
    return [from, to, commonIndent(lines.slice(from, to))];
  }, [lines, tab.part]);
  const pane = useRef<HTMLPreElement>(null);
  useFitCode(pane, tab.source.file, 12, 11);

  return (
    <div className="panel code-panel">
      <div className="panel-title">
        Code
        {tabs.length === 1 ? (
          <span className="file">{tab.source.file}</span>
        ) : (
          tabs.map((t) => (
            <button key={t.key} className={`file-tab ${t.key === tab.key ? "active" : ""}`} onClick={() => onPick?.(t.key)}>
              {t.source.file.split("/").pop()}
            </button>
          ))
        )}
        {aside && <span className="panel-aside">{aside}</span>}
      </div>
      <pre ref={pane} className="code">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          const body = line.slice(indent);
          const on = lit != null && i === lit.line;
          return (
            <div
              key={i}
              className={`line ${on ? "lit" : ""}`}
              style={on ? { borderLeftColor: lit.color, background: `color-mix(in srgb, ${lit.color} 15%, transparent)` } : undefined}
            >
              <span className="gutter">{i + 1}</span>
              <span className="text" style={hangingIndent(body)}>
                {highlight(body)}
              </span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}
