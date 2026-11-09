"use client";

import { useEffect, useState } from "react";
import { HUE, NEUTRAL, alpha } from "@/lib/hue";

// The code under a demo, lit as the run goes, folded until the reader opens it. It folds the same way as the
// sample's "real code" box under it. A code panel inside re-fits its font when it opens (useFitCode observes its size).
//
// `pulse` is anything that changes when what is inside changes, such as the demo's step. While the fold is closed,
// each change tints its bar for a moment in `color`, the colour the demo gives that moment, so a reader can see the
// code is moving with the run without the bar flashing at them.
export default function CodeFold({
  label,
  pulse,
  color = HUE.green,
  children,
}: {
  label: string;
  pulse?: unknown;
  color?: string;
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(false);
  // Counts the changes seen, and the last one whose light has gone out. Pass a string or a number: an object made
  // afresh on every render would count as a change each time.
  const [seen, setSeen] = useState({ pulse, count: 0 });
  if (!Object.is(seen.pulse, pulse)) setSeen({ pulse, count: seen.count + 1 });
  const [faded, setFaded] = useState(0);
  useEffect(() => {
    if (seen.count === faded) return;
    const timer = setTimeout(() => setFaded(seen.count), 1100);
    return () => clearTimeout(timer);
  }, [seen.count, faded]);
  const lit = seen.count !== faded;
  const showing = lit && !open;

  return (
    <details
      onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}
      className="group rounded-lg border border-border bg-bg-secondary text-sm transition-colors duration-500"
      style={{ borderColor: showing ? alpha(color, 35) : undefined }}
    >
      <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-4 py-3 text-text-secondary hover:text-text-primary">
        <span className="flex items-center gap-2.5">
          {pulse !== undefined && (
            <span
              aria-hidden="true"
              className="inline-block size-2 rounded-full transition-colors duration-500"
              style={{ background: showing ? color : NEUTRAL.lineStrong }}
            />
          )}
          {label}
          <span className={`text-xs transition-opacity duration-500 ${showing ? "opacity-70" : "opacity-0"}`} style={{ color }}>
            changed
          </span>
        </span>
        <span aria-hidden="true" className="text-text-muted transition-transform group-open:rotate-90">
          &rsaquo;
        </span>
      </summary>
      <div className="border-t border-border p-3">{children}</div>
    </details>
  );
}
