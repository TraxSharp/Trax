// Whether a moving picture can be seen, and which layout the screen takes, without React, so the tests can drive them
// with a fake screen. useOnScreen and useMediaQuery (src/components/landing/useOnScreen.ts) wrap them.

export interface ScreenEnv {
  IntersectionObserver: typeof IntersectionObserver;
  document: Pick<Document, "visibilityState" | "addEventListener" | "removeEventListener">;
}

/**
 * Calls `onChange` each time the node comes into view in a visible tab, or stops being either. It keeps watching for
 * as long as it runs, so a picture scrolled away or in a hidden tab stops, and starts again when it is back. Returns
 * a function that stops watching.
 */
export function watchOnScreen(node: Element, onChange: (on: boolean) => void, env: ScreenEnv, threshold = 0.3): () => void {
  let inView = false;
  let last = false;
  const report = () => {
    const on = inView && env.document.visibilityState === "visible";
    if (on !== last) {
      last = on;
      onChange(on);
    }
  };
  const observer = new env.IntersectionObserver(
    ([entry]) => {
      inView = entry.isIntersecting;
      report();
    },
    { threshold },
  );
  observer.observe(node);
  env.document.addEventListener("visibilitychange", report);
  return () => {
    observer.disconnect();
    env.document.removeEventListener("visibilitychange", report);
  };
}

/** Calls `onChange` with whether the media query matches each time that changes. Returns a function that stops. */
export function watchMedia(query: string, onChange: (matches: boolean) => void, matchMedia: (query: string) => MediaQueryList): () => void {
  const list = matchMedia(query);
  const changed = () => onChange(list.matches);
  list.addEventListener("change", changed);
  return () => list.removeEventListener("change", changed);
}
