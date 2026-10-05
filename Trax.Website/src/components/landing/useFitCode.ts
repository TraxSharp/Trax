"use client";

import { useLayoutEffect, type RefObject } from "react";

/**
 * Keeps a code panel's lines whole, the way an editor shows them: sizes the font so the longest line fits the
 * panel's width, between `min` and `max` pixels. Below `min` the panel scrolls sideways instead of wrapping, so a
 * chain never breaks mid-call. The element must be the panel's scroll container and its lines must not wrap.
 * `shown` names what the panel shows, such as its file: a change re-fits it.
 */
export function useFitCode(ref: RefObject<HTMLElement | null>, shown: string, max = 12, min = 10): void {
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const fit = () => {
      let size = max;
      // Two passes: the padding and the line-number gutter do not all scale with the font.
      for (let pass = 0; pass < 2; pass++) {
        el.style.fontSize = `${size}px`;
        const ratio = el.clientWidth / el.scrollWidth;
        if (ratio >= 1) break;
        size = Math.max(min, Math.floor(size * ratio * 10) / 10);
      }
      el.style.fontSize = `${size}px`;
    };
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(el);
    return () => observer.disconnect();
  }, [ref, shown, max, min]);
}
