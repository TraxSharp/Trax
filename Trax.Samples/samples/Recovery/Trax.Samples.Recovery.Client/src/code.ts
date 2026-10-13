// How the code panels lay out a C# file, the way traxsharp.net's demos do: the chains re-indented so each step's
// name sits in one column, and a line too wide for the panel soft-wrapped under itself.

import type { CSSProperties } from "react";

/**
 * Re-indents a file's chains with the step names in one column: the line that starts a chain (`Chain<...>` or a
 * machine's `m.In(...)`) moves one space in, and the lines that continue it move four spaces out, so each `.Step`
 * hangs its dot to the left of the name above it. Whitespace only: the lines and their numbers stay the file's.
 */
export function alignChains(lines: string[]): string[] {
  let start = -1;
  return lines.map((line) => {
    const indent = line.search(/\S/);
    const text = line.trim();
    if (text.startsWith("Chain<") || text.startsWith("m.In(")) {
      start = indent;
      return " " + line;
    }
    if (start < 0 || indent < 0) return line;
    if (indent >= start + 4) return line.slice(4);
    start = -1;
    return line;
  });
}

/**
 * How a line wraps when it is too wide for the panel even at the smallest font: its continuation hangs under it, a
 * comment flush with itself and code four characters further in. The indent stays as real spaces, so a copy keeps
 * it; the padding and its negative first-line indent only place the wrapped part.
 */
export function hangingIndent(text: string): CSSProperties {
  const lead = Math.max(0, text.search(/\S/));
  const hang = text.slice(lead).startsWith("//") ? lead : lead + 4;
  return { paddingLeft: `${hang}ch`, textIndent: `-${hang}ch` };
}

/** The lines a code panel shows: from the first line `starts` matches through the last line `ends` matches after it. */
export function shownRange(lines: string[], starts: (line: string) => boolean, ends: (line: string) => boolean): [number, number] {
  const at = lines.findIndex(starts);
  let end = -1;
  lines.forEach((l, i) => {
    if (i > at && end < 0 && ends(l)) end = i;
  });
  return at < 0 || end < 0 ? [0, lines.length] : [at, end + 1];
}

/** The smallest indent of the non-blank lines in a range, which the panel strips from each. */
export function commonIndent(lines: string[]): number {
  const indents = lines.filter((l) => l.trim()).map((l) => l.search(/\S/));
  return indents.length ? Math.min(...indents) : 0;
}
