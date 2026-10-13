import { useLayoutEffect, type RefObject } from "react";

/**
 * Keeps a code panel's lines whole, the way an editor shows them: sizes the font so the longest line fits the
 * panel's width, between `min` and `max` pixels. Below `min` a line soft-wraps (see hangingIndent). `shown` names
 * what the panel shows, such as its file: a change fits it again.
 */
export function useFitCode(ref: RefObject<HTMLElement | null>, shown: string, max = 12, min = 11): void {
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const fit = () => {
      // Measured with every line unwrapped (data-fit="measure" in index.css), so wrapping is the last resort. Scale
      // by the overflow first, then step down until the longest line fits: the padding and the line-number gutter do
      // not scale with the font, so one proportional step can leave a few pixels over.
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
