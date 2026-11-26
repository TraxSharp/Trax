"use client";

import { useEffect, useRef, useState, useSyncExternalStore, type RefObject } from "react";
import { watchMedia, watchOnScreen } from "@/lib/on-screen";

/**
 * Whether the node is in view in a visible tab, now: false until the page has looked, and again whenever it is not.
 * `onFirstSight` runs once, the first time it is: where a picture starts playing on its own.
 */
export function useOnScreen(ref: RefObject<Element | null>, onFirstSight?: () => void, threshold = 0.3): boolean {
  const [on, setOn] = useState(false);
  const first = useRef(onFirstSight);
  useEffect(() => {
    first.current = onFirstSight;
  });
  useEffect(() => {
    const node = ref.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    let seen = false;
    const changed = (now: boolean) => {
      setOn(now);
      if (now && !seen) {
        seen = true;
        first.current?.();
      }
    };
    return watchOnScreen(node, changed, { IntersectionObserver, document }, threshold);
  }, [ref, threshold]);
  return on;
}

/** Whether the media query matches, or null while the page is rendered on the server and has no screen to ask. */
export function useMediaQuery(query: string): boolean | null {
  return useSyncExternalStore(
    (changed) => watchMedia(query, changed, (q) => window.matchMedia(q)),
    () => window.matchMedia(query).matches,
    () => null,
  );
}
