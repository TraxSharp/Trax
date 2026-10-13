"use client";

import { useLayoutEffect, type CSSProperties, type RefObject } from "react";

/**
 * Keeps a code panel's lines whole, the way an editor shows them: sizes the font so the longest line fits the
 * panel's width, between `min` and `max` pixels. Below `min` a line soft-wraps (codeLineLayout).
 * `shown` names what the panel shows, such as its file: a change re-fits it.
 */
export function useFitCode(ref: RefObject<HTMLElement | null>, shown: string, max = 12, min = 10): void {
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const fit = () => {
      // Scale by the overflow first, then step down until the longest line fits: the padding and the line-number
      // gutter do not scale with the font, so one proportional step can still leave a few pixels over.
      // Measured with every line unwrapped (see globals.css), so wrapping is the last resort, not the first.
      el.dataset.fit = "measure";
      let size = max;
      el.style.fontSize = `${size}px`;
      if (el.scrollWidth > el.clientWidth) {
        size = Math.max(min, Math.floor(size * (el.clientWidth / el.scrollWidth) * 10) / 10);
        el.style.fontSize = `${size}px`;
      }
      while (size > min && el.scrollWidth > el.clientWidth) {
        size = Math.max(min, Math.round((size - 0.2) * 10) / 10);
        el.style.fontSize = `${size}px`;
      }
      delete el.dataset.fit;
    };
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(el);
    return () => observer.disconnect();
  }, [ref, shown, max, min]);
}

/**
 * How a code panel lays out one line, the way an editor soft-wraps: a line too wide for the panel, even at
 * useFitCode's smallest size, wraps, its continuation indented under it, so the panel never scrolls sideways.
 * A comment wraps flush with itself; code hangs four characters further in.
 */
export function codeLineLayout(text: string): { row: string; text: string; style: CSSProperties; body: string } {
  const lead = Math.max(0, text.search(/\S/));
  const comment = text.slice(lead).startsWith("//");
  // The indent stays as real spaces, so a copy keeps it; the padding and its negative first-line indent only
  // place the wrapped continuation.
  const hang = comment ? lead : lead + 4;
  return {
    row: "w-full",
    text: "min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere]",
    style: { paddingLeft: `${hang}ch`, textIndent: `-${hang}ch` },
    body: text,
  };
}

/**
 * Re-indents a sample's chains the way the site writes them, with the step names in one column: the line that
 * starts a chain (`Chain<...>` or a machine's `m.In(...)`) moves one space in, and the lines that continue it move
 * four spaces out, so each `.Step` hangs its dot to the left of the name above it. Whitespace only: the lines and
 * their numbers stay the file's.
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
