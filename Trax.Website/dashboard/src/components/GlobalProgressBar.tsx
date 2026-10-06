import { useActivity } from "../lib/activity";

// Thin bar pinned to the top of the viewport that appears whenever any query or mutation is in
// flight. Gives a universal "something is happening" signal for the whole app, so long operations
// (acknowledging thousands of dead letters, the heavy metrics query, navigation reads) always show
// activity even when a specific button has no busy state of its own.
export function GlobalProgressBar() {
  const active = useActivity();
  if (!active) return null;
  return (
    <div
      role="progressbar"
      aria-label="Loading"
      aria-busy="true"
      className="fixed top-0 left-0 right-0 h-0.5 z-[100] bg-accent animate-pulse pointer-events-none"
    />
  );
}
